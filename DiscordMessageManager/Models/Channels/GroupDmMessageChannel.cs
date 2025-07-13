namespace DiscordMessageManager.Models.Channels;

public record GroupDmMessageChannel(ulong ChannelId, string Name) : MessageChannelBase(ChannelId, Name);