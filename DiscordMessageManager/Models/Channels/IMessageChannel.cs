namespace DiscordMessageManager.Models.Channels;

public interface IMessageChannel
{
    ulong ChannelId { get; }
    string Name { get; }
    ChannelMessage[] Messages { get; }
}