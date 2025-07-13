namespace DiscordMessageManager.Models.Channels;

public record DmMessageChannel(ulong? UserId, ulong ChannelId, string Name) : MessageChannelBase(ChannelId, Name);