using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscordMessageManager.Models;
using DiscordMessageManager.Models.Channels;

namespace DiscordPurger.ViewModels;

public partial class ChannelViewModel : ViewModelBase
{
    private readonly IMessageChannel _channel;
    private readonly bool[] _selection;

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private string _messageSearchText = "";
    
    public ObservableCollection<MessageItemViewModel> Messages { get; } = new();
    public ObservableCollection<MessageItemViewModel> FilteredMessages { get; } = new();

    public ChannelViewModel(IMessageChannel channel)
    {
        _channel = channel;
        _selection = new bool[channel.Messages.Length];
        
        // Initialize message view models
        foreach (var msg in channel.Messages)
        {
            var msgVM = new MessageItemViewModel(msg);
            msgVM.PropertyChanged += (_, e) => {
                if (e.PropertyName == nameof(MessageItemViewModel.IsSelected))
                {
                    UpdateSelection();
                }
            };
            Messages.Add(msgVM);
        }
        
        FilterMessages();
        InitializeCommands();
    }

    // Design-time constructor
    public ChannelViewModel()
    {
        _channel = null!;
        _selection = Array.Empty<bool>();
        InitializeCommands();
    }

    public string Name => _channel?.Name ?? "Design Channel";
    public int Count => _channel?.Messages.Length ?? 0;
    public int SelectedCount => Messages.Count(m => m.IsSelected);

    public IEnumerable<ChannelMessage> SelectedMessages =>
        Messages.Where(m => m.IsSelected).Select(m => m.Message);

    public event Action? SelectionChanged;

    partial void OnIsSelectedChanged(bool value)
    {
        if (value) SelectAll();
        else SelectNone();
    }

    public void SelectAll()
    {
        foreach (var msg in Messages)
            msg.IsSelected = true;
    }

    public void SelectNone()
    {
        foreach (var msg in Messages)
            msg.IsSelected = false;
    }
    
    private void InitializeCommands()
    {
        SelectAllMsgsCommand = new RelayCommand(() => {
            foreach (var msg in FilteredMessages)
                msg.IsSelected = true;
        });
        
        SelectNoMsgsCommand = new RelayCommand(() => {
            foreach (var msg in FilteredMessages)
                msg.IsSelected = false;
        });
        
        ShowModalCommand = new RelayCommand(() => IsVisible = true);
        CloseModalCommand = new RelayCommand(() => IsVisible = false);
        ApplySelectionCommand = new RelayCommand(() => {
            UpdateSelection();
            IsVisible = false;
        });
    }
    
    public ICommand SelectAllMsgsCommand { get; private set; } = null!;
    public ICommand SelectNoMsgsCommand { get; private set; } = null!;
    public ICommand ShowModalCommand { get; private set; } = null!;
    public ICommand CloseModalCommand { get; private set; } = null!;
    public ICommand ApplySelectionCommand { get; private set; } = null!;
    
    partial void OnMessageSearchTextChanged(string value)
    {
        FilterMessages();
    }
    
    private void FilterMessages()
    {
        var search = MessageSearchText?.ToLower() ?? "";
        var filtered = string.IsNullOrEmpty(search)
            ? Messages.ToList()
            : Messages.Where(m => m.Content.ToLower().Contains(search) || 
                                 m.Id.ToLower().Contains(search)).ToList();
        
        FilteredMessages.Clear();
        foreach (var msg in filtered)
            FilteredMessages.Add(msg);
    }
    
    private void UpdateSelection()
    {
        // Update the old selection array for compatibility
        for (int i = 0; i < _selection.Length && i < Messages.Count; i++)
        {
            _selection[i] = Messages[i].IsSelected;
        }
        
        OnPropertyChanged(nameof(SelectedCount));
        SelectionChanged?.Invoke();
    }
}