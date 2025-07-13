using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using DiscordMessageManager.Models;
using Polly;
using Polly.Retry;

namespace DiscordMessageManager.Api;

internal class DiscordRestApi
{
    private const string BaseUrl = "https://discord.com/api/v9/";
    private const int MaxRetries = 5;

    private static readonly HttpClientHandler ClientHandler = new()
    {
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
    };

    private static readonly HttpClient Client = new(ClientHandler, false)
    {
        BaseAddress = new Uri(BaseUrl),
        Timeout = TimeSpan.FromSeconds(30)
    };

    private static readonly AsyncRetryPolicy<HttpResponseMessage> RetryPolicy = Policy
        .HandleResult<HttpResponseMessage>(r => r.StatusCode == (HttpStatusCode)429)
        .WaitAndRetryAsync(MaxRetries,
            (retryCount, response, context) =>
            {
                TimeSpan backoff;

                try
                {
                    var content = response.Result.Content.ReadAsStringAsync()
                        .GetAwaiter()
                        .GetResult();

                    using var document = JsonDocument.Parse(content);
                    var root = document.RootElement;

                    if (root.TryGetProperty("retry_after", out var retryAfterProp)
                        && retryAfterProp.ValueKind == JsonValueKind.Number
                        && retryAfterProp.TryGetDouble(out var seconds))
                        backoff = TimeSpan.FromSeconds(seconds);
                    else
                        backoff = TimeSpan.FromSeconds(5);
                }
                catch
                {
                    backoff = TimeSpan.FromSeconds(5);
                }

                return backoff;
            },
            async (response, timespan, retryCount, context) =>
            {
                var randomDelay = 1.05 + RandomNumberGenerator.GetInt32(0, int.MaxValue) / (double)int.MaxValue *
                    (1.30 - 1.05);
                await Task.Delay(timespan * randomDelay); // Extra time
            }
        );

    private readonly string _token;

    public DiscordRestApi(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            throw new ArgumentException("Token must be provided.", nameof(token));

        _token = token.Trim();
        InitializeDefaultHeaders();
    }

    private void InitializeDefaultHeaders()
    {
        var headers = new Dictionary<string, string>
        {
            { "Accept", "*/*" },
            { "Accept-Language", "en-US,en;q=0.5" },
            { "Authorization", _token },
            { "Connection", "keep-alive" },
            { "DNT", "1" },
            { "Origin", "https://discord.com" },
            { "Priority", "u=0" },
            { "Sec-Fetch-Dest", "empty" },
            { "Sec-Fetch-Mode", "cors" },
            { "Sec-Fetch-Site", "same-origin" },
            { "Sec-GPC", "1" },
            { "TE", "trailers" },
            { "User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:128.0) Gecko/20100101 Firefox/128.0" },
            { "X-Debug-Options", "bugReporterEnabled" }
        };

        foreach (var kvp in headers) Client.DefaultRequestHeaders.TryAddWithoutValidation(kvp.Key, kvp.Value);
    }

    public async Task<MessageDeletionResult> DeleteMessageAsync(
        ulong channelId,
        ulong messageId,
        CancellationToken cancellationToken = default)
    {
        return MessageDeletionResult.Deleted;
        // TODO later
        var uri = $"channels/{channelId}/messages/{messageId}";

        using var request = new HttpRequestMessage(HttpMethod.Delete, uri);
        var response = await SendRequestAsync(request, cancellationToken).ConfigureAwait(false);

        var status = response.StatusCode;
        return status switch
        {
            HttpStatusCode.NotFound => MessageDeletionResult.NotFound,
            HttpStatusCode.NoContent or
                HttpStatusCode.OK or
                HttpStatusCode.Accepted or
                HttpStatusCode.NonAuthoritativeInformation => MessageDeletionResult.Deleted,
            HttpStatusCode.Unauthorized or
                HttpStatusCode.Forbidden => MessageDeletionResult.NoPermissions,
            _ => MessageDeletionResult.Timeout
        };
    }

    private static async Task<HttpResponseMessage> SendRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return await RetryPolicy.ExecuteAsync(
                ct => Client.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct),
                cancellationToken)
            .ConfigureAwait(false);
    }
}