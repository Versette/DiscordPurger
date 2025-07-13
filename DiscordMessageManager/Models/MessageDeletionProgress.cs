namespace DiscordMessageManager.Models;

public record MessageDeletionProgress(
    int ProcessedCount,
    int TotalCount,
    MessageDeletionResult LastResult,
    ChannelMessage LastMessage)
{
    public double PercentageComplete => TotalCount > 0 ? (double)ProcessedCount / TotalCount : 0.0;
    public bool IsComplete => ProcessedCount >= TotalCount;
}