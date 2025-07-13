using DiscordMessageManager.Models.Channels;

namespace DiscordMessageManager.Models;

public record ChannelMessage(
    ulong Id,
    DateTime Timestamp,
    string? Contents,
    string? Attachments,
    IMessageChannel Channel);