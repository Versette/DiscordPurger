using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using DiscordMessageManager.Models;
using DiscordMessageManager.Models.Channels;

namespace DiscordMessageManager.Services;

[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class DataPackageJsonContext : JsonSerializerContext
{
}

public static class DataPackageReader
{
    // TODO: Finish porting all to zip reading
    // TODO: FIX XML DOCS TO ACCOUNT FOR CHANGES THAT CAME FROM SWITCHING TO ZIP READING
    private static readonly string[] RequiredChannelFolderFiles = ["channel.json", "messages.json"];

    public static async Task<DataPackage> ReadDataPackageAsync(string dataPackagePath,
        IProgress<DataPackageProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataPackagePath);

        if (!File.Exists(dataPackagePath)) throw new FileNotFoundException(dataPackagePath);

        using var archive = ZipFile.OpenRead(dataPackagePath);
        const int totalSteps = 4;

        // Step 1: Load avatar
        progress?.Report(new DataPackageProgress(1, totalSteps, 0, 0, "Loading avatar...", 0));
        cancellationToken.ThrowIfCancellationRequested();

        byte[]? avatarBytes = null;
        await using var avatarStream = archive.Entries.FirstOrDefault(x => x.Name == "avatar.png")?.Open();
        if (avatarStream != null)
        {
            using var ms = new MemoryStream();
            await avatarStream.CopyToAsync(ms, cancellationToken);
            avatarBytes = ms.ToArray();
        }

        // Step 2: Find messages path
        progress?.Report(new DataPackageProgress(2, totalSteps, 0, 0, "Reading package structure...", 25));
        cancellationToken.ThrowIfCancellationRequested();

        var messagesFolder = FindMessagesPath(archive);

        // Step 3: Load channel descriptions
        progress?.Report(new DataPackageProgress(3, totalSteps, 0, 0, "Loading channel metadata...", 50));
        cancellationToken.ThrowIfCancellationRequested();

        var channelDescriptions = await GetChannelDescriptionsAsync(archive, messagesFolder);

        // Step 4: Process channels with progress reporting
        progress?.Report(new DataPackageProgress(4, totalSteps, 0, channelDescriptions.Count, "Processing channels...",
            75));

        var messageChannels =
            await GetMessageChannelsAsync(archive, messagesFolder, channelDescriptions, progress, cancellationToken);

        progress?.Report(new DataPackageProgress(totalSteps, totalSteps, channelDescriptions.Count,
            channelDescriptions.Count, "Complete", 100));

        return new DataPackage(0, null, null, avatarBytes, messageChannels);
    }

    public static async Task<DataPackage> ReadDataPackageAsync(string dataPackagePath)
    {
        return await ReadDataPackageAsync(dataPackagePath, null, CancellationToken.None);
    }

    private static string? GetChannelNameFromDescription(string description)
    {
        const string dmStart = "Direct Message with ";

        if (!description.StartsWith(dmStart)) return description.Split(" in ")[0];

        var dmUsername = description[dmStart.Length..];
        if (dmUsername.EndsWith("#0"))
            return dmUsername[..^2];

        return dmUsername;
    }

    private static async Task<IMessageChannel[]> GetMessageChannelsAsync(
        ZipArchive archive,
        string channelsPath,
        Dictionary<ulong, string> channelDescriptions,
        IProgress<DataPackageProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var channelEntries = archive.Entries
            .Where(e => e.FullName.StartsWith($"{channelsPath}/c", StringComparison.Ordinal)
                        && e.FullName.EndsWith('/'))
            .ToList();

        var channels = new List<IMessageChannel>();
        var processedCount = 0;

        // Use SemaphoreSlim to control concurrency properly with async operations
        var maxConcurrency = Math.Min(Environment.ProcessorCount, channelEntries.Count);
        using var semaphore = new SemaphoreSlim(maxConcurrency, maxConcurrency);

        var tasks = channelEntries.Select(async entry => // TODO: Extract to separate load method
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var channel = await ProcessChannelAsync(archive, entry, channelDescriptions, cancellationToken)
                    .ConfigureAwait(false);

                if (channel != null)
                    lock (channels)
                    {
                        channels.Add(channel);
                    }

                var currentCount = Interlocked.Increment(ref processedCount);
                var percentage = 75 + currentCount * 25.0 / channelEntries.Count;

                progress?.Report(new DataPackageProgress(4, 4, currentCount, channelEntries.Count,
                    $"Processing channel {currentCount}/{channelEntries.Count}", percentage));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                // Log error but continue processing other channels
                // TODO: Add proper logging
                Interlocked.Increment(ref processedCount);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        return channels.Where(x => x.Messages.Length > 0).ToArray();
    }

    private static async Task<IMessageChannel?> ProcessChannelAsync(
        ZipArchive archive,
        ZipArchiveEntry channelEntry,
        Dictionary<ulong, string> channelDescriptions,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var channelData = await LoadChannelDataAsync(archive, channelEntry.FullName, cancellationToken);

        var channelId = channelData.TryGetProperty("id", out var idProp) &&
                        ulong.TryParse(idProp.GetString(), out var id)
            ? id
            : throw new InvalidDataException("Channel ID is missing or invalid");

        var channelType = channelData.TryGetProperty("type", out var typeProp) &&
                          typeProp.ValueKind == JsonValueKind.String
            ? typeProp.GetString()
            : throw new InvalidDataException("Channel type is missing");

        if (!channelDescriptions.TryGetValue(channelId, out var description))
            throw new KeyNotFoundException($"Channel description not found for ID: {channelId}");

        var channelName = GetChannelNameFromDescription(description);

        return channelType switch
        {
            "GUILD_TEXT" => await CreateGuildChannelAsync(archive, channelEntry.FullName, channelData, channelId,
                channelName, cancellationToken),
            "DM" => await CreateDmChannelAsync(archive, channelEntry.FullName, channelData, channelId, channelName,
                cancellationToken),
            "GROUP_DM" => await CreateGroupDmChannelAsync(archive, channelEntry.FullName, channelData, channelId,
                channelName, cancellationToken),
            _ => throw new NotSupportedException($"Channel type '{channelType}' is not supported")
        };
    }

    private static async Task<ChannelMessage[]> LoadMessagesAsync(ZipArchive archive, string channelPath,
        IMessageChannel channel, CancellationToken cancellationToken = default)
    {
        var messagesEntry = archive.GetEntry($"{channelPath}messages.json")
                            ?? throw new FileNotFoundException($"Messages file not found for channel: {channelPath}");

        await using var stream = messagesEntry.Open();
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        using var document = JsonDocument.Parse(json);

        var messages = new List<ChannelMessage>();
        foreach (var messageElement in document.RootElement.EnumerateArray())
        {
            var messageId = messageElement.TryGetProperty("ID", out var idProp) &&
                            idProp.ValueKind == JsonValueKind.Number
                ? idProp.GetUInt64()
                : throw new InvalidDataException("Message id is missing");

            var messageTimestamp = messageElement.TryGetProperty("Timestamp", out var timestampProp) &&
                                   timestampProp.ValueKind == JsonValueKind.String &&
                                   DateTime.TryParse(timestampProp.GetString(), out var timestamp)
                ? timestamp
                : throw new InvalidDataException("Message timestamp is missing");

            var messageContents = messageElement.TryGetProperty("Contents", out var contentsProp) &&
                                  contentsProp.ValueKind == JsonValueKind.String
                ? contentsProp.GetString()
                : string.Empty;

            var messageAttachments = messageElement.TryGetProperty("Attachments", out var attachmentsProp) &&
                                     attachmentsProp.ValueKind == JsonValueKind.String
                ? attachmentsProp.GetString()
                : string.Empty;

            messages.Add(new ChannelMessage(messageId, messageTimestamp, messageContents, messageAttachments, channel));
        }

        return messages.ToArray();
    }

    private static async Task<JsonElement> LoadChannelDataAsync(ZipArchive archive, string channelPath,
        CancellationToken cancellationToken = default)
    {
        var channelEntry = archive.GetEntry($"{channelPath}channel.json")
                           ?? throw new FileNotFoundException($"Channel file not found: {channelPath}");

        await using var stream = channelEntry.Open();
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private static async Task<GuildMessageChannel> CreateGuildChannelAsync(
        ZipArchive archive,
        string channelPath,
        JsonElement channelData,
        ulong channelId,
        string channelName,
        CancellationToken cancellationToken = default)
    {
        Guild? guild = null;

        if (channelData.TryGetProperty("guild", out var guildData))
        {
            var guildId = guildData.TryGetProperty("id", out var guildIdProp) &&
                          ulong.TryParse(guildIdProp.GetString(), out var gId)
                ? gId
                : throw new InvalidDataException("Guild ID is missing or invalid");

            var guildName = guildData.TryGetProperty("name", out var guildNameProp) &&
                            guildNameProp.ValueKind == JsonValueKind.String
                ? guildNameProp.GetString()
                : throw new InvalidDataException("Guild name is missing");

            guild = new Guild(guildId, guildName!);
        }

        var channel = new GuildMessageChannel(channelId, channelName, guild);
        var messages = await LoadMessagesAsync(archive, channelPath, channel, cancellationToken);
        channel.MessagesInternal.AddRange(messages);
        return channel;
    }

    private static async Task<DmMessageChannel> CreateDmChannelAsync(
        ZipArchive archive,
        string channelPath,
        JsonElement channelData,
        ulong channelId,
        string channelName,
        CancellationToken cancellationToken = default)
    {
        if (!channelData.TryGetProperty("recipients", out var recipientsProp) ||
            recipientsProp.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("Recipients array is missing");

        if (recipientsProp.GetArrayLength() == 0)
            throw new InvalidDataException("Recipients array is empty");

        ulong? recipientId = null;
        if (recipientsProp.GetArrayLength() > 0)
        {
            var firstRecipient = recipientsProp.EnumerateArray().First();
            if (firstRecipient.ValueKind == JsonValueKind.String &&
                ulong.TryParse(firstRecipient.GetString(), out var id))
                recipientId = id;
        }

        var channel = new DmMessageChannel(recipientId, channelId, channelName);
        var messages = await LoadMessagesAsync(archive, channelPath, channel, cancellationToken);
        channel.MessagesInternal.AddRange(messages);
        return channel;
    }

    private static async Task<GroupDmMessageChannel> CreateGroupDmChannelAsync(
        ZipArchive archive,
        string channelPath,
        JsonElement channelData,
        ulong channelId,
        string? channelName,
        CancellationToken cancellationToken = default)
    {
        var channel = new GroupDmMessageChannel(channelId, channelName ?? "Group Chat");
        var messages = await LoadMessagesAsync(archive, channelPath, channel, cancellationToken);
        channel.MessagesInternal.AddRange(messages);
        return channel;
    }

    private static async Task<Dictionary<ulong, string>> GetChannelDescriptionsAsync(ZipArchive archive,
        string messagesFolder)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(messagesFolder);

        var entryPath = Path.Combine(messagesFolder, "index.json").Replace('\\', '/');
        var entry = archive.GetEntry(entryPath)
                    ?? throw new InvalidDataException($"Required file '{entryPath}' was not found in the archive.");

        await using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync().ConfigureAwait(false);

        var channelMap =
            JsonSerializer.Deserialize(json,
                DataPackageJsonContext.Default.DictionaryStringString) // TODO: Replace DataPackageJsonContext
            ?? throw new JsonException("Failed to deserialize channels index: result was null.");

        return channelMap
            .Select(kvp =>
            {
                if (!ulong.TryParse(kvp.Key, out var id))
                    throw new FormatException(
                        $"Invalid channel ID '{kvp.Key}': expected a valid unsigned long integer.");

                return (id, kvp.Value);
            })
            .ToDictionary();
    }

    private static string FindMessagesPath(ZipArchive archive)
    {
        ArgumentNullException.ThrowIfNull(archive);

        const string channelPrefix = "c";

        var validParentFolder = archive.Entries
            .Where(e => !string.IsNullOrEmpty(e.FullName))
            .Select(e => e.FullName.Replace('\\', '/').Split('/'))
            .Where(parts => parts.Length >= 3 && parts[1].StartsWith(channelPrefix))
            .GroupBy(parts => new { Parent = parts[0], Channel = parts[1] })
            .Where(g => RequiredChannelFolderFiles.All(req => g.Any(parts => parts.Length > 2 && parts[2] == req)))
            .Select(g => g.Key.Parent)
            .FirstOrDefault();

        return validParentFolder
               ?? throw new InvalidDataException(
                   "No valid messages directory structure was found in the data package.");
    }
}