using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Kodi;

/// <summary>
/// Reads the playback position from Kodi over JSON-RPC: every <see cref="PollInterval"/> and right after Kodi announces
/// a pause, resume, seek or new playback.
/// </summary>
/// <remarks>
/// <para>Kodi reports the position to the millisecond; Jellyfin for Kodi only reports whole seconds and rarely. Each
/// reading is time-stamped at the middle of its round trip, with half the round trip as its uncertainty.</para>
/// <para>When Kodi cannot be reached (remote control off, firewall) the clock gives no readings, says why in
/// <see cref="Description"/> and tries again every <see cref="RetryInterval"/>. When the file Kodi plays carries a
/// different Jellyfin item id, the clock stops: that Kodi plays something else.</para>
/// <para>It only reads (<c>Player.GetActivePlayers</c>, <c>Player.GetItem</c>, <c>Player.GetProperties</c>).</para>
/// </remarks>
internal sealed partial class KodiClock : IPlaybackClock
{
    /// <summary>How often the position is read while nothing is announced.</summary>
    internal static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    /// <summary>How long to wait before connecting again after Kodi could not be reached.</summary>
    internal static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(30);

    /// <summary>How long connecting may take.</summary>
    internal static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

    /// <summary>How long Kodi may take to answer a request.</summary>
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(2);

    /// <summary>Added to every reading's uncertainty: Kodi's clock moves in steps of about one audio packet.</summary>
    internal static readonly TimeSpan ReadingTolerance = TimeSpan.FromMilliseconds(20);

    /// <summary>Longest time <see cref="DisposeAsync"/> waits for the clock's task.</summary>
    internal static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(5);

    private readonly KodiClockOptions _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _stopping = new();
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });
    private ClockReading? _latest;
    private string _description;
    private int _playerChanged;
    private Task _run = Task.CompletedTask;
    private int _disposed;

    private KodiClock(KodiClockOptions options, TimeProvider timeProvider, ILogger logger)
    {
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
        _description = $"Connecting to Kodi at {options.Endpoint}.";
    }

    /// <inheritdoc />
    public ClockReading? Latest => Volatile.Read(ref _latest);

    /// <inheritdoc />
    public string Description => Volatile.Read(ref _description);

    /// <summary>Gets the clock's task (lets tests wait for it to end).</summary>
    internal Task Completion => _run;

    /// <summary>Starts reading the position on a background task.</summary>
    /// <param name="options">Which Kodi, and for which item.</param>
    /// <param name="timeProvider">Clock for polling, retries and timeouts.</param>
    /// <param name="logger">Logger.</param>
    /// <returns>The running clock.</returns>
    public static KodiClock Start(KodiClockOptions options, TimeProvider timeProvider, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);
        var clock = new KodiClock(options, timeProvider, logger);
        clock._run = Task.Run(clock.RunAsync);
        return clock;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _stopping.CancelAsync().ConfigureAwait(false);
        try
        {
            await _run.WaitAsync(StopTimeout, _timeProvider).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            return; // It ends in the background; the session must not stall on it.
        }

        _stopping.Dispose();
    }

    /// <summary>Gets the Jellyfin item id in a file Kodi plays (add-on playback streams from <c>/Videos/{id}/</c>).</summary>
    /// <param name="file">The file or URL.</param>
    /// <returns>The id, or <see langword="null"/> when the file does not name one (a direct path).</returns>
    internal static Guid? ItemIdInFile(string? file)
    {
        var match = file is null ? null : ItemIdPattern().Match(file);
        return match is { Success: true } && Guid.TryParse(match.Groups[1].ValueSpan, out var id) ? id : null;
    }

    [GeneratedRegex("/(?:Videos|Items)/([0-9a-fA-F]{32}|[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})(?:/|\\?|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ItemIdPattern();

    // Kodi's answers are read defensively: a missing or differently typed property is "unknown", never an exception.
    private static JsonElement? TryGetProperty(JsonElement element, string name)
        => element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) ? value : null;

    private static string? TryGetString(JsonElement element, string name)
        => TryGetProperty(element, name) is { ValueKind: JsonValueKind.String } value ? value.GetString() : null;

    private static int? TryGetInt32(JsonElement element, string name)
        => TryGetProperty(element, name) is { ValueKind: JsonValueKind.Number } value && value.TryGetInt32(out var number) ? number : null;

    [SuppressMessage(
        "Design",
        "CA1031:Do not catch general exception types",
        Justification = "Top level of the clock's task: a failure must only end the precise timing, never the session, and must be logged.")]
    private async Task RunAsync()
    {
        var token = _stopping.Token;
        var warned = false;
        try
        {
            while (!token.IsCancellationRequested)
            {
                var connected = false;
                try
                {
                    var connection = await KodiJsonRpcConnection.ConnectAsync(
                        _options.Host, _options.Port, ConnectTimeout, OnNotification, _timeProvider, token).ConfigureAwait(false);
                    await using (connection.ConfigureAwait(false))
                    {
                        connected = true;
                        Log.Connected(_logger, _options.Endpoint);
                        if (!await FollowAsync(connection, token).ConfigureAwait(false))
                        {
                            return; // Kodi plays another item.
                        }
                    }
                }
                catch (KodiException ex) when (connected)
                {
                    Volatile.Write(ref _latest, null);
                    SetDescription($"Lost the connection to Kodi at {_options.Endpoint}; reconnecting.");
                    Log.Lost(_logger, _options.Endpoint, ex.Message);
                }
                catch (KodiException ex)
                {
                    Volatile.Write(ref _latest, null);
                    SetDescription($"Kodi at {_options.Endpoint} is not reachable ({ex.Message}). For precise timing, turn on "
                        + "Settings → Services → Control → Allow remote control from applications on other systems in Kodi.");
                    if (warned)
                    {
                        Log.StillUnreachable(_logger, _options.Endpoint, ex.Message);
                    }
                    else
                    {
                        warned = true;
                        Log.Unreachable(_logger, _options.Endpoint, ex.Message);
                    }
                }

                await Task.Delay(RetryInterval, _timeProvider, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // The session ended.
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _latest, null);
            SetDescription("Reading Kodi's clock failed unexpectedly; using Jellyfin's reports.");
            Log.Unexpected(_logger, ex);
        }
    }

    /// <summary>Reads the position until the connection is lost (throws) or Kodi plays another item (returns false).</summary>
    private async Task<bool> FollowAsync(KodiJsonRpcConnection connection, CancellationToken token)
    {
        int? player = null;
        while (true)
        {
            if (Interlocked.Exchange(ref _playerChanged, 0) != 0)
            {
                player = null; // A new playback started in Kodi: find its player and check its item again.
            }

            if (player is null && await FindVideoPlayerAsync(connection, token).ConfigureAwait(false) is { } found)
            {
                if (await PlaysOtherItemAsync(connection, found, token).ConfigureAwait(false))
                {
                    Volatile.Write(ref _latest, null);
                    SetDescription($"Kodi at {_options.Endpoint} plays another item; using Jellyfin's reports.");
                    Log.OtherItem(_logger, _options.Endpoint);
                    return false;
                }

                player = found;
            }

            var reading = player is { } id ? await ReadAsync(connection, id, token).ConfigureAwait(false) : null;
            if (reading is null)
            {
                player = null;
                Volatile.Write(ref _latest, null);
                SetDescription($"Connected to Kodi at {_options.Endpoint}; waiting for its video player.");
            }
            else
            {
                Volatile.Write(ref _latest, reading);
                SetDescription($"Kodi's clock at {_options.Endpoint}.");
            }

            await WaitForNextReadingAsync(token).ConfigureAwait(false);
        }
    }

    private async Task<int?> FindVideoPlayerAsync(KodiJsonRpcConnection connection, CancellationToken token)
    {
        var players = await connection.CallAsync("Player.GetActivePlayers", null, RequestTimeout, _timeProvider, token).ConfigureAwait(false);
        if (players.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var player in players.EnumerateArray())
        {
            if (TryGetString(player, "type") == "video" && TryGetInt32(player, "playerid") is { } playerId)
            {
                return playerId;
            }
        }

        return null;
    }

    private async Task<bool> PlaysOtherItemAsync(KodiJsonRpcConnection connection, int player, CancellationToken token)
    {
        JsonElement result;
        try
        {
            result = await connection.CallAsync(
                "Player.GetItem",
                writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("playerid", player);
                    writer.WriteStartArray("properties");
                    writer.WriteStringValue("file");
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                },
                RequestTimeout,
                _timeProvider,
                token).ConfigureAwait(false);
        }
        catch (KodiErrorException)
        {
            return false; // The player just stopped; the next reading finds out.
        }

        var file = TryGetProperty(result, "item") is { } item ? TryGetString(item, "file") : null;
        return ItemIdInFile(file) is { } playing && playing != _options.ItemId;
    }

    private async Task<ClockReading?> ReadAsync(KodiJsonRpcConnection connection, int player, CancellationToken token)
    {
        var sent = _timeProvider.GetUtcNow();
        JsonElement result;
        try
        {
            result = await connection.CallAsync(
                "Player.GetProperties",
                writer =>
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("playerid", player);
                    writer.WriteStartArray("properties");
                    writer.WriteStringValue("time");
                    writer.WriteStringValue("speed");
                    writer.WriteEndArray();
                    writer.WriteEndObject();
                },
                RequestTimeout,
                _timeProvider,
                token).ConfigureAwait(false);
        }
        catch (KodiErrorException)
        {
            return null; // The player stopped.
        }

        var received = _timeProvider.GetUtcNow();
        if (TryGetProperty(result, "time") is not { } time || TryGetInt32(result, "speed") is not { } speed)
        {
            return null;
        }

        var position = new TimeSpan(
            0,
            TryGetInt32(time, "hours") ?? 0,
            TryGetInt32(time, "minutes") ?? 0,
            TryGetInt32(time, "seconds") ?? 0,
            TryGetInt32(time, "milliseconds") ?? 0);
        var halfRoundTrip = (received - sent) / 2;
        return new ClockReading(position, speed == 0, sent + halfRoundTrip, halfRoundTrip + ReadingTolerance);
    }

    private async Task WaitForNextReadingAsync(CancellationToken token)
    {
        using var poll = new CancellationTokenSource(PollInterval, _timeProvider);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, poll.Token);
        try
        {
            await _wake.Reader.ReadAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            // Time for the regular reading.
        }
    }

    private void OnNotification(string method)
    {
        if (!method.StartsWith("Player.", StringComparison.Ordinal))
        {
            return;
        }

        if (method is "Player.OnPlay" or "Player.OnAVStart" or "Player.OnStop")
        {
            Volatile.Write(ref _playerChanged, 1);
        }

        _wake.Writer.TryWrite(true); // Read right away (pause, resume, seek, speed, new playback).
    }

    private void SetDescription(string description) => Volatile.Write(ref _description, description);

    private static partial class Log
    {
        [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Reading the playback position from Kodi at {Endpoint}")]
        public static partial void Connected(ILogger logger, string endpoint);

        [LoggerMessage(EventId = 2, Level = LogLevel.Information, Message = "Kodi at {Endpoint} does not accept remote control ({Reason}); the timing uses Jellyfin's reports. For precise timing turn on Settings → Services → Control → Allow remote control from applications on other systems in Kodi")]
        public static partial void Unreachable(ILogger logger, string endpoint, string reason);

        [LoggerMessage(EventId = 3, Level = LogLevel.Debug, Message = "Kodi at {Endpoint} is still not reachable: {Reason}")]
        public static partial void StillUnreachable(ILogger logger, string endpoint, string reason);

        [LoggerMessage(EventId = 4, Level = LogLevel.Information, Message = "Kodi at {Endpoint} plays another item; the timing uses Jellyfin's reports")]
        public static partial void OtherItem(ILogger logger, string endpoint);

        [LoggerMessage(EventId = 5, Level = LogLevel.Error, Message = "Reading Kodi's clock failed unexpectedly; the timing uses Jellyfin's reports")]
        public static partial void Unexpected(ILogger logger, Exception exception);

        [LoggerMessage(EventId = 6, Level = LogLevel.Debug, Message = "Lost the connection to Kodi at {Endpoint} ({Reason}); reconnecting")]
        public static partial void Lost(ILogger logger, string endpoint, string reason);
    }
}
