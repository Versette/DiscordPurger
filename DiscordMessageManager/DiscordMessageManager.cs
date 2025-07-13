using DiscordMessageManager.Api;
using DiscordMessageManager.Models;

namespace DiscordMessageManager;

public class DiscordMessageManager
{
    private const int MessagesPerMinute = 25;
    private readonly TimeSpan _messageDeletionInterval = TimeSpan.FromSeconds(60.0 / MessagesPerMinute);
    private readonly DiscordRestApi _restApi;

    public DiscordMessageManager(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        _restApi = new DiscordRestApi(token);
    }

    public async Task<MessageDeletionResult> DeleteMessageAsync(ulong channelId, ulong messageId)
    {
        return await _restApi.DeleteMessageAsync(channelId, messageId);
    }

    public async Task<MessageDeletionResult> DeleteMessageAsync(ChannelMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        return await DeleteMessageAsync(message.Channel.ChannelId, message.Id);
    }

    public async Task<IReadOnlyList<MessageDeletionResult>> DeleteMessagesAsync(
        IEnumerable<ChannelMessage> messages,
        IProgress<MessageDeletionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var messageList = messages.ToList();
        var results = new List<MessageDeletionResult>(messageList.Count);

        for (var i = 0; i < messageList.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await DeleteMessageAsync(messageList[i]);
            results.Add(result);

            progress?.Report(new MessageDeletionProgress(i + 1, messageList.Count, result, messageList[i]));

            if (i < messageList.Count - 1)
                await Task.Delay(_messageDeletionInterval, cancellationToken);
        }

        return results;
    }

    public Task<IReadOnlyList<MessageDeletionResult>> DeleteMessagesAsync(
        ChannelMessage[] messages,
        IProgress<MessageDeletionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return DeleteMessagesAsync(messages, progress, cancellationToken);
    }
}