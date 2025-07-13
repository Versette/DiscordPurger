using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DiscordPurger.Models;

namespace DiscordPurger.Services;

public partial class NotificationService : ObservableObject
{
    [ObservableProperty] private ObservableCollection<NotificationItem> _notifications = [];

    public NotificationService()
    {
        ClearAllCommand = new RelayCommand(ClearAll);
    }

    public ICommand ClearAllCommand { get; }

    public void AddNotification(string message, NotificationType type = NotificationType.Info)
    {
        var notification = new NotificationItem(message, type);
        notification.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(NotificationItem.IsVisible) && !notification.IsVisible)
            {
                var item = Notifications.FirstOrDefault(n => n == notification);
                if (item != null) Notifications.Remove(item);
            }
        };

        Notifications.Insert(0, notification);

        if (Notifications.Count > 50)
        {
            var toRemove = Notifications.Skip(50).ToList();
            foreach (var item in toRemove) Notifications.Remove(item);
        }
    }

    public void AddInfo(string message)
    {
        AddNotification(message);
    }

    public void AddSuccess(string message)
    {
        AddNotification(message, NotificationType.Success);
    }

    public void AddWarning(string message)
    {
        AddNotification(message, NotificationType.Warning);
    }

    public void AddError(string message)
    {
        AddNotification(message, NotificationType.Error);
    }

    private void ClearAll()
    {
        Notifications.Clear();
    }
}