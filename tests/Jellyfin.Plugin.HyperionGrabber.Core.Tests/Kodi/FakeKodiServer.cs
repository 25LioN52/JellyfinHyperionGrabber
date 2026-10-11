using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Kodi;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.Kodi;

/// <summary>
/// Kodi's JSON-RPC TCP server on loopback: answers <c>Player.GetActivePlayers</c>, <c>Player.GetItem</c> and
/// <c>Player.GetProperties</c> from settable state, pushes notifications, and writes every answer in two pieces
/// with a notification glued in front, like a busy Kodi, so the client's framing is exercised.
/// </summary>
internal sealed class FakeKodiServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stopping = new();
    private readonly ConcurrentDictionary<int, TcpClient> _clients = new();
    private readonly Task _accepting;
    private long _timeTicks;
    private volatile int _speed = 1;
    private volatile bool _hasVideoPlayer = true;
    private volatile string? _file;
    private int _connections;

    private FakeKodiServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        _accepting = AcceptAsync();
    }

    public string Host => ((IPEndPoint)_listener.LocalEndpoint).Address.ToString();

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public int ConnectionCount => Volatile.Read(ref _connections);

    /// <summary>Gets or sets the position the player reports.</summary>
    public TimeSpan Time
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref _timeTicks));
        set => Interlocked.Exchange(ref _timeTicks, value.Ticks);
    }

    /// <summary>Gets or sets the playback speed (0 = paused).</summary>
    public int Speed
    {
        get => _speed;
        set => _speed = value;
    }

    /// <summary>Gets or sets a value indicating whether a video player is active.</summary>
    public bool HasVideoPlayer
    {
        get => _hasVideoPlayer;
        set => _hasVideoPlayer = value;
    }

    /// <summary>Gets or sets the file the player plays.</summary>
    public string? File
    {
        get => _file;
        set => _file = value;
    }

    public static FakeKodiServer Start() => new();

    /// <summary>Sends a notification to every connected client.</summary>
    public async Task NotifyAsync(string method)
    {
        var bytes = Encoding.UTF8.GetBytes($"{{\"jsonrpc\":\"2.0\",\"method\":\"{method}\",\"params\":{{\"data\":{{\"player\":{{\"playerid\":1}}}},\"sender\":\"xbmc\"}}}}");
        foreach (var client in _clients.Values)
        {
            await client.GetStream().WriteAsync(bytes);
        }
    }

    /// <summary>Closes every connection.</summary>
    public void DropConnections()
    {
        foreach (var (id, client) in _clients)
        {
            _clients.TryRemove(id, out _);
            client.Dispose();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        _listener.Stop();
        DropConnections();
        try
        {
            await _accepting;
        }
        catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
        {
            System.Diagnostics.Debug.WriteLine($"Ignored expected exception during FakeKodiServer disposal: {ex}");
        }

        _stopping.Dispose();
    }

    private static (int Id, string Method) ParseRequest(ReadOnlyMemory<byte> request)
    {
        using var document = JsonDocument.Parse(request);
        return (document.RootElement.GetProperty("id").GetInt32(), document.RootElement.GetProperty("method").GetString()!);
    }

    private async Task AcceptAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(_stopping.Token);
            var id = Interlocked.Increment(ref _connections);
            _clients[id] = client;
            _ = ServeAsync(client);
        }
    }

    private async Task ServeAsync(TcpClient client)
    {
        using var _ = client;
        var splitter = new JsonObjectSplitter(1 << 16);
        try
        {
            var stream = client.GetStream();
            while (true)
            {
                var read = await stream.ReadAsync(splitter.GetBuffer(), _stopping.Token);
                if (read == 0)
                {
                    return;
                }

                splitter.Advance(read);
                while (splitter.TryRead(out var request))
                {
                    var (id, method) = ParseRequest(request);
                    var answer = Encoding.UTF8.GetBytes(
                        "{\"jsonrpc\":\"2.0\",\"method\":\"System.OnIdle\",\"params\":{\"data\":\"{not json}\",\"sender\":\"xbmc\"}}" + Answer(id, method));
                    var half = answer.Length / 2;
                    await stream.WriteAsync(answer.AsMemory(0, half), _stopping.Token);
                    await stream.FlushAsync(_stopping.Token);
                    await stream.WriteAsync(answer.AsMemory(half), _stopping.Token);
                }
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or System.IO.IOException or SocketException or ObjectDisposedException)
        {
            // The client or the test closed the connection.
        }
    }

    private string Answer(int id, string method)
    {
        var (time, speed, hasPlayer, file) = (Time, Speed, HasVideoPlayer, File);

        var result = method switch
        {
            "Player.GetActivePlayers" => hasPlayer ? "[{\"playerid\":1,\"playertype\":\"internal\",\"type\":\"video\"}]" : "[]",
            "Player.GetItem" when hasPlayer => $"{{\"item\":{{\"file\":{JsonSerializer.Serialize(file ?? string.Empty)},\"label\":\"Big Buck Bunny\",\"type\":\"movie\"}}}}",
            "Player.GetProperties" when hasPlayer => string.Create(
                CultureInfo.InvariantCulture,
                $"{{\"speed\":{speed},\"time\":{{\"hours\":{(int)time.TotalHours},\"milliseconds\":{time.Milliseconds},\"minutes\":{time.Minutes},\"seconds\":{time.Seconds}}}}}"),
            _ => null,
        };
        return result is null
            ? $"{{\"error\":{{\"code\":-32100,\"message\":\"Failed to execute method.\"}},\"id\":{id},\"jsonrpc\":\"2.0\"}}"
            : $"{{\"id\":{id},\"jsonrpc\":\"2.0\",\"result\":{result}}}";
    }
}
