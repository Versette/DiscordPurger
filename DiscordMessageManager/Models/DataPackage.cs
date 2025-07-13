using DiscordMessageManager.Models.Channels;

namespace DiscordMessageManager.Models;

public record DataPackage(
    ulong UserId,
    string DisplayName,
    string Username,
    byte[]? Avatar,
    IMessageChannel[] MessageChannels);