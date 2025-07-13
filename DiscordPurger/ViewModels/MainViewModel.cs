using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscordMessageManager.Models;
using DiscordMessageManager.Models.Channels;
using DiscordMessageManager.Services;
using DiscordPurger.Models;
using DiscordPurger.Services;

namespace DiscordPurger.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly Timer _filterTimer;
    private readonly SemaphoreSlim _uiSema = new(1, 1);
    private CancellationTokenSource? _cancelToken;
    private CancellationTokenSource? _loadCancelToken;
    private DataPackage? _dataPackage;

    [ObservableProperty] private string _dataPath = "";
    [ObservableProperty] private int _deleted;
    [ObservableProperty] private int _failed;
    private bool _filtering;
    [ObservableProperty] private bool _isLoaded;
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isLoggedIn;
    [ObservableProperty] private bool _isPurging;
    public NotificationService NotificationService { get; } = new();
    public LoginViewModel LoginViewModel { get; } = new();
    private DiscordMessageManager.DiscordMessageManager? _msgManager;
    [ObservableProperty] private int _processed;
    [ObservableProperty] private double _progress;
    [ObservableProperty] private double _purgeProgress;
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private string _status = "Ready";
    [ObservableProperty] private string _token = "";
    [ObservableProperty] private User? _user;

    public MainViewModel()
    {
        _filterTimer = new Timer(_ => Filter(), null, Timeout.Infinite, Timeout.Infinite);

        LoadCmd = new AsyncRelayCommand(LoadPackage, () => !IsLoading);
        LoginCmd = new RelayCommand(ShowLogin);
        StartCmd = new AsyncRelayCommand(Start, () => IsLoaded && IsLoggedIn && SelectedMessages.Any() && !IsPurging);
        StopCmd = new RelayCommand(Stop, () => IsPurging);
    }

    public ObservableCollection<ChannelViewModel> Channels { get; } = [];
    public ObservableCollection<ChannelViewModel> FilteredChannels { get; } = [];
    public ObservableCollection<ChannelMessage> SelectedMessages { get; } = [];

    public TopLevel? TopLevel { get; set; }

    public string Stats =>
        $"Total: {Channels.Sum(c => c.Count)} | Selected: {SelectedMessages.Count} | Channels: {Channels.Count}";

    public AsyncRelayCommand LoadCmd { get; }
    public ICommand LoginCmd { get; }
    public AsyncRelayCommand StartCmd { get; }
    public RelayCommand StopCmd { get; }
    public ICommand SelectAllCmd => new RelayCommand(() => FilteredChannels.ToList().ForEach(c => c.IsSelected = true));

    public ICommand SelectNoneCmd =>
        new RelayCommand(() => FilteredChannels.ToList().ForEach(c => c.IsSelected = false));

    public ICommand ToggleCmd => new RelayCommand<ChannelViewModel>(c =>
    {
        if (c != null) c.IsSelected = !c.IsSelected;
    });



    partial void OnIsLoadingChanged(bool value)
    {
        LoadCmd.NotifyCanExecuteChanged();
    }

    partial void OnIsLoggedInChanged(bool value)
    {
        StartCmd.NotifyCanExecuteChanged();
    }

    partial void OnIsPurgingChanged(bool value)
    {
        StartCmd.NotifyCanExecuteChanged();
        StopCmd.NotifyCanExecuteChanged();
    }

    partial void OnSearchTextChanged(string value)
    {
        _filterTimer.Change(300, Timeout.Infinite);
    }

    private async Task LoadPackage()
    {
        if (TopLevel?.StorageProvider is not { } storage)
        {
            Status = "File picker not available - TopLevel not set";
            return;
        }

        // Cancel any existing load operation
        _loadCancelToken?.Cancel();
        _loadCancelToken = new CancellationTokenSource();
        var cancellationToken = _loadCancelToken.Token;
        
        try
        {
            // Browse for file
            var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = "Select Discord Data Package",
                FileTypeFilter = [new FilePickerFileType("Discord Data") { Patterns = ["*.zip"] }]
            });

            if (files.Count == 0)
            {
                Status = "No file selected";
                return;
            }

            var filePath = files[0].Path.LocalPath;
            DataPath = filePath;
            
            if (!File.Exists(filePath))
            {
                Status = "File not found";
                return;
            }

            // Load the package asynchronously
            IsLoading = true;
            Progress = 0;
            
            // Get file size for display
            var fileInfo = new FileInfo(filePath);
            var fileSizeMB = fileInfo.Length / (1024.0 * 1024.0);
            Status = $"Loading {fileSizeMB:F1}MB data package...";

            // Load data package on background thread with progress reporting
            var progressReporter = new Progress<DataPackageProgress>(progress =>
            {
                if (cancellationToken.IsCancellationRequested) return;
                
                Dispatcher.UIThread.Post(() =>
                {
                    Progress = progress.PercentageComplete;
                    Status = progress.StatusMessage;
                });
            });
            
            _dataPackage = await DataPackageReader.ReadDataPackageAsync(filePath, progressReporter, cancellationToken);
            
            var totalChannels = _dataPackage.MessageChannels.Length;
            var totalMessages = _dataPackage.MessageChannels.Sum(c => c.Messages.Length);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Channels.Clear();
                FilteredChannels.Clear();
                SelectedMessages.Clear();
            });

            // Create channel ViewModels efficiently - no need for additional processing
            Status = $"Creating views for {totalChannels} channels ({totalMessages:N0} messages)";
            Progress = 0;

            // Simply wrap the already-processed channels in ViewModels
            await Task.Run(() =>
            {
                var channelViewModels = _dataPackage.MessageChannels
                    .AsParallel()
                    .WithDegreeOfParallelism(Environment.ProcessorCount)
                    .WithCancellation(cancellationToken)
                    .Select(ch =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var vm = new ChannelViewModel(ch);
                        vm.SelectionChanged += RefreshSelected;
                        return vm;
                    })
                    .ToList();

                // Add all ViewModels to UI in one batch
                Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var vm in channelViewModels)
                    {
                        Channels.Add(vm);
                    }
                    Progress = 100;
                    Status = $"Loaded {totalChannels} channels";
                });
            }, cancellationToken);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Filter();
                IsLoaded = true;
                Status = $"Loaded {Channels.Sum(c => c.Count):N0} messages from {Channels.Count} channels";
                OnPropertyChanged(nameof(Stats));
            });
        }
        catch (OperationCanceledException)
        {
            Status = "Loading cancelled";
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            Progress = 0;
            _loadCancelToken?.Dispose();
            _loadCancelToken = null;
        }
    }

    private void ShowLogin()
    {
        LoginViewModel.Show();
        
        // Subscribe to login completion
        LoginViewModel.PropertyChanged += OnLoginViewModelPropertyChanged;
    }
    
    private void OnLoginViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LoginViewModel.IsVisible) && !LoginViewModel.IsVisible)
        {
            // Unsubscribe to avoid memory leaks
            LoginViewModel.PropertyChanged -= OnLoginViewModelPropertyChanged;
            
            // Check if login was successful
            if (LoginViewModel.AuthUser != null)
            {
                User = LoginViewModel.AuthUser;
                Token = LoginViewModel.AuthToken ?? "";
                IsLoggedIn = true;
                Status = $"Logged in as {User.DisplayName}";
            }
        }
    }

    private async Task Start()
    {
        if (string.IsNullOrEmpty(Token)) return;

        try
        {
            _msgManager = new DiscordMessageManager.DiscordMessageManager(Token);
            _cancelToken = new CancellationTokenSource();
            IsPurging = true;
            Deleted = Failed = Processed = 0;
            PurgeProgress = 0;
            Status = "Starting purge...";
            NotificationService.AddInfo("Starting purge...");

            var progress = new Progress<MessageDeletionProgress>(p =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    Processed = p.ProcessedCount;
                    PurgeProgress = p.PercentageComplete * 100;

                    switch (p.LastResult)
                    {
                        case MessageDeletionResult.Deleted: Deleted++; break;
                        case MessageDeletionResult.NotFound:
                        case MessageDeletionResult.NoPermissions:
                        case MessageDeletionResult.Timeout: Failed++; break;
                    }

                    Status = $"Purging: {Processed}/{p.TotalCount} ({PurgeProgress:F0}%)";
                    
                    var notificationType = p.LastResult switch
                    {
                        MessageDeletionResult.Deleted => NotificationType.Success,
                        MessageDeletionResult.NotFound => NotificationType.Warning,
                        MessageDeletionResult.NoPermissions => NotificationType.Warning,
                        MessageDeletionResult.Timeout => NotificationType.Error,
                        _ => NotificationType.Info
                    };
                    
                    NotificationService.AddNotification(
                        $"[{p.LastResult}] {p.LastMessage.Channel.Name}: {p.LastMessage.Id}",
                        notificationType);
                });
            });

            await _msgManager.DeleteMessagesAsync(SelectedMessages.ToArray(), progress, _cancelToken.Token);
            Status = "Purge completed";
            NotificationService.AddSuccess($"Completed. Deleted: {Deleted}, Failed: {Failed}");
        }
        catch (OperationCanceledException)
        {
            Status = "Purge cancelled";
            NotificationService.AddWarning("Cancelled by user");
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
            NotificationService.AddError($"Error: {ex.Message}");
        }
        finally
        {
            IsPurging = false;
            _cancelToken?.Dispose();
            _cancelToken = null;
        }
    }

    private void Stop()
    {
        _cancelToken?.Cancel();
    }

    private void Filter()
    {
        if (_filtering) return;
        _filtering = true;

        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                var search = SearchText?.ToLower() ?? "";
                var filtered = string.IsNullOrEmpty(search)
                    ? Channels.ToList()
                    : Channels.Where(c => c.Name.ToLower().Contains(search)).ToList();

                FilteredChannels.Clear();
                foreach (var ch in filtered) FilteredChannels.Add(ch);
                OnPropertyChanged(nameof(Stats));
            });
        }
        finally
        {
            _filtering = false;
        }
    }

    private void RefreshSelected()
    {
        if (!_uiSema.Wait(0)) return;

        try
        {
            Task.Run(() => Dispatcher.UIThread.Post(() =>
            {
                SelectedMessages.Clear();
                foreach (var msg in Channels.Where(c => c.IsSelected).SelectMany(c => c.SelectedMessages))
                    SelectedMessages.Add(msg);
                OnPropertyChanged(nameof(Stats));
                StartCmd.NotifyCanExecuteChanged();
            }));
        }
        finally
        {
            _uiSema.Release();
        }
    }


    private void LogAdd(string msg)
    {
        NotificationService.AddInfo(msg);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _filterTimer?.Dispose();
            _uiSema?.Dispose();
            _cancelToken?.Dispose();
            _loadCancelToken?.Dispose();
        }

        base.Dispose(disposing);
    }
}