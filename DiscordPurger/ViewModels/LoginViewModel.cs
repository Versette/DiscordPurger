using System;
using System.Threading.Tasks;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscordPurger.Models;
using DiscordPurger.Services;

namespace DiscordPurger.ViewModels;

public partial class LoginViewModel : ViewModelBase
{
    private readonly AuthService _auth = new();
    [ObservableProperty] private bool _isLoading;
    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private string _status = "";
    [ObservableProperty] private string _token = "";
    
    private bool _isClosing;
    public bool IsClosing
    {
        get => _isClosing;
        set => SetProperty(ref _isClosing, value);
    }

    public bool HasStatus => !string.IsNullOrEmpty(Status);
    public bool HasUser => AuthUser != null;

    public User? AuthUser { get; private set; }
    public string? AuthToken { get; private set; }

    public ICommand LoginCommand => new AsyncRelayCommand(Login);
    public ICommand CloseCommand => new RelayCommand(Close);
    public ICommand ContinueCommand => new RelayCommand(Continue);

    private async Task Login()
    {
        if (string.IsNullOrWhiteSpace(Token))
        {
            Status = "Enter token";
            return;
        }

        try
        {
            IsLoading = true;
            Status = "Authenticating...";

            var user = await _auth.GetUserAsync(Token);
            if (user != null)
            {
                AuthUser = user;
                AuthToken = Token;
                Status = $"Logged in as {user.DisplayName}";
                OnPropertyChanged(nameof(HasUser));
            }
            else
            {
                Status = "Authentication failed";
            }
        }
        catch (Exception ex)
        {
            Status = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasStatus));
        }
    }

    public void Show()
    {
        IsVisible = true;
        Token = "";
        Status = "";
        AuthUser = null;
        AuthToken = null;
        OnPropertyChanged(nameof(HasUser));
        OnPropertyChanged(nameof(HasStatus));
    }

    private async void Close()
    {
        IsClosing = true;
        await Task.Delay(200); // Wait for fade-out animation
        IsVisible = false;
        IsClosing = false;
    }

    private async void Continue()
    {
        IsClosing = true;
        await Task.Delay(200); // Wait for fade-out animation
        IsVisible = false;
        IsClosing = false;
    }
}