namespace DiscordMessageManager.Models;

public enum MessageDeletionResult
{
    NoPermissions,
    NotFound,
    Deleted,
    Timeout
}