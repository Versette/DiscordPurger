namespace DiscordMessageManager.Models.Channels;

public record GuildMessageChannel(ulong ChannelId, string Name, Guild? Guild) : MessageChannelBase(ChannelId, Name);