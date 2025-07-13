namespace DiscordMessageManager.Models.Channels;

public abstract record MessageChannelBase(ulong ChannelId, string Name) : IMessageChannel
{
    internal List<ChannelMessage> MessagesInternal { get; } = [];
    public ChannelMessage[] Messages => MessagesInternal.ToArray();
}