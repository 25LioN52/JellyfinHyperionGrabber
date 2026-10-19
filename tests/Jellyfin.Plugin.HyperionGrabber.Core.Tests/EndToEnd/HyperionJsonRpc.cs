using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.EndToEnd;

/// <summary>
/// Minimal JSON-RPC connection to Hyperion.ng or HyperHDR on TCP port 19444: one JSON object per line in both
/// directions, replies matched to their request by <c>tan</c>. The commands live in <see cref="HyperionServerApi"/>,
/// because the two servers differ in them.
/// </summary>
public sealed class HyperionJsonRpc : IAsyncDisposable
{
    private static readonly TimeSpan ReplyTimeout = TimeSpan.FromSeconds(15);

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly StreamReader _reader;
    private int _tan;

    private HyperionJsonRpc(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
        _reader = new StreamReader(_stream, Encoding.UTF8);
    }

    public static async Task<HyperionJsonRpc> ConnectAsync(string host, int port, CancellationToken cancellationToken)
    {
        var client = new TcpClient { NoDelay = true };
        try
        {
            await client.ConnectAsync(host, port, cancellationToken);
        }
        catch
        {
            client.Dispose();
            throw;
        }

        return new HyperionJsonRpc(client);
    }

    /// <summary>Whether a message is the reply to, or a streamed update of, the request with this <c>tan</c>.</summary>
    public static bool HasTan(JsonElement message, int tan)
        => message.TryGetProperty("tan", out var value) && value.ValueKind == JsonValueKind.Number && value.GetInt32() == tan;

    /// <summary>Sends a request and returns its successful reply.</summary>
    /// <exception cref="HyperionJsonRpcException">The server answered with <c>success: false</c>.</exception>
    public async Task<JsonElement> SendAsync(JsonObject request, CancellationToken cancellationToken)
    {
        var tan = ++_tan;
        request["tan"] = tan;
        await _stream.WriteAsync(Encoding.UTF8.GetBytes(request.ToJsonString() + "\n"), cancellationToken);

        // Skips replies to earlier requests (Hyperion.ng's getconfig without a filter answers twice), unsolicited
        // updates and streamed messages, which carry the tan of the request that started the stream.
        var reply = await ReadAsync(message => HasTan(message, tan), cancellationToken);
        if (!reply.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
        {
            throw new HyperionJsonRpcException($"The server rejected {request["command"]} {request["subcommand"]}: {reply.GetRawText()}");
        }

        return reply;
    }

    /// <summary>Reads messages until one matches and returns it; the ones before it are dropped.</summary>
    public async Task<JsonElement> ReadAsync(Func<JsonElement, bool> match, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReplyTimeout);
        while (true)
        {
            var line = await _reader.ReadLineAsync(timeout.Token)
                ?? throw new IOException("The server closed the JSON-RPC connection.");
            using var document = JsonDocument.Parse(line);
            if (match(document.RootElement))
            {
                return document.RootElement.Clone();
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _reader.Dispose();
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>The server answered a JSON-RPC request with <c>success: false</c>.</summary>
public sealed class HyperionJsonRpcException : Exception
{
    public HyperionJsonRpcException()
    {
    }

    public HyperionJsonRpcException(string message)
        : base(message)
    {
    }

    public HyperionJsonRpcException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
