using System;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DiscordPurger.Models;

public enum NotificationType
{
    Info,
    Success,
    Warning,
    Error
}

public partial class NotificationItem : ObservableObject
{
    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private string _message = "";
    [ObservableProperty] private DateTime _timestamp;
    [ObservableProperty] private NotificationType _type;

    public NotificationItem(string message, NotificationType type = NotificationType.Info)
    {
        Message = message;
        Type = type;
        Timestamp = DateTime.Now;
        CloseCommand = new RelayCommand(Close);
    }

    public string Icon => Type switch
    {
        NotificationType.Success => "✓",
        NotificationType.Warning => "⚠",
        NotificationType.Error => "✗",
        _ => "ℹ"
    };

    public string IconColor => Type switch
    {
        NotificationType.Success => "#28A745",
        NotificationType.Warning => "#FFC107",
        NotificationType.Error => "#DC3545",
        _ => "#17A2B8"
    };

    public ICommand CloseCommand { get; }

    private void Close()
    {
        IsVisible = false;
    }
}