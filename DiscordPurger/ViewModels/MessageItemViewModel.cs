using System;
using CommunityToolkit.Mvvm.ComponentModel;
using DiscordMessageManager.Models;

namespace DiscordPurger.ViewModels;

public partial class MessageItemViewModel : ObservableObject
{
    [ObservableProperty] private bool _isSelected;

    public MessageItemViewModel(ChannelMessage message)
    {
        Message = message;
    }

    public string Id => Message.Id.ToString();
    public DateTime Timestamp => Message.Timestamp;

    public string Content => string.IsNullOrWhiteSpace(Message.Contents)
        ? "[No content]"
        : Message.Contents.Length > 200
            ? Message.Contents[..200] + "..."
            : Message.Contents;

    public ChannelMessage Message { get; }
}