namespace DiscordMessageManager.Models;

public record DataPackageProgress(
    int CurrentStep,
    int TotalSteps,
    int ProcessedChannels,
    int TotalChannels,
    string StatusMessage,
    double PercentageComplete);