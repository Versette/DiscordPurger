namespace DiscordPurger.Models;

public class User
{
    public ulong Id { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? Avatar { get; set; }

    public string AvatarUrl => Avatar != null
        ? $"https://cdn.discordapp.com/avatars/{Id}/{Avatar}.png"
        : $"https://cdn.discordapp.com/embed/avatars/{Id % 5}.png";
}