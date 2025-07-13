using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Threading.Tasks;
using DiscordPurger.Models;

namespace DiscordPurger.Services;

public class AuthService
{
    private static readonly JsonSerializerOptions SerializerOptions = new JsonSerializerOptions
    {
        TypeInfoResolver = JsonTypeInfoResolver.Combine(
            new DefaultJsonTypeInfoResolver())
    };
    
    private static readonly HttpClient Client = new()
    {
        BaseAddress = new Uri("https://discord.com/api/v9/"),
        Timeout = TimeSpan.FromSeconds(10)
    };

    public async Task<User?> GetUserAsync(string token)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "users/@me");
            req.Headers.Add("Authorization", token);
            req.Headers.Add("User-Agent",
                "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:128.0) Gecko/20100101 Firefox/128.0");

            var res = await Client.SendAsync(req);
            if (!res.IsSuccessStatusCode) return null;

            var json = await res.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<JsonElement>(json, SerializerOptions);

            return new User
            {
                Id = ulong.TryParse(data.GetProperty("id").GetString(), out var id) ? id : 0,
                Username = data.GetProperty("username").GetString() ?? "",
                DisplayName = data.TryGetProperty("global_name", out var gn) && gn.ValueKind != JsonValueKind.Null
                    ? gn.GetString() ?? ""
                    : data.GetProperty("username").GetString() ?? "",
                Avatar = data.TryGetProperty("avatar", out var av) && av.ValueKind != JsonValueKind.Null
                    ? av.GetString()
                    : null
            };
        }
        catch
        {
            return null;
        }
    }
}