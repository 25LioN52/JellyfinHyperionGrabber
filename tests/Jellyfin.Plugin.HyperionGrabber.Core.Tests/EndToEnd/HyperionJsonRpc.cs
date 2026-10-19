using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Diagnostics;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.EndToEnd;

/// <summary>
/// Minimal client for Hyperion.ng's JSON-RPC API on TCP port 19444: one JSON object per line in both directions,
/// replies matched to their request by <c>tan</c>. Used by the end-to-end tests to look inside a real Hyperion.
/// </summary>
/// <remarks>
/// Commands and reply shapes are those of Hyperion.ng 2.2 (<c>libsrc/api/JsonAPI.cpp</c>, <c>JsonInfo.cpp</c>).
/// Commands that take <c>instance</c> run against that instance; the others are global.
/// </remarks>
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

    /// <summary>Sends a request and returns its successful reply.</summary>
    /// <exception cref="HyperionJsonRpcException">Hyperion answered with <c>success: false</c>.</exception>
    public async Task<JsonElement> SendAsync(JsonObject request, CancellationToken cancellationToken)
    {
        var tan = ++_tan;
        request["tan"] = tan;
        await _stream.WriteAsync(Encoding.UTF8.GetBytes(request.ToJsonString() + "\n"), cancellationToken);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ReplyTimeout);
        while (true)
        {
            var line = await _reader.ReadLineAsync(timeout.Token)
                ?? throw new IOException("Hyperion closed the JSON-RPC connection.");
            using var document = JsonDocument.Parse(line);
            var reply = document.RootElement;

            // Skip replies to earlier requests (getconfig without a filter answers twice) and unsolicited updates.
            if (!reply.TryGetProperty("tan", out var replyTan) || replyTan.ValueKind != JsonValueKind.Number || replyTan.GetInt32() != tan)
            {
                continue;
            }

            if (!reply.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
            {
                throw new HyperionJsonRpcException($"Hyperion rejected {request["command"]} {request["subcommand"]}: {line}");
            }

            return reply.Clone();
        }
    }

    public async Task<JsonElement> GetServerInfoAsync(int instance, CancellationToken cancellationToken)
    {
        var reply = await SendAsync(new JsonObject { ["command"] = "serverinfo", ["instance"] = instance }, cancellationToken);
        return reply.GetProperty("info");
    }

    /// <summary>Returns the instances Hyperion knows, running or not.</summary>
    public async Task<IReadOnlyList<HyperionInstanceInfo>> GetInstancesAsync(CancellationToken cancellationToken)
    {
        var reply = await SendAsync(new JsonObject { ["command"] = "serverinfo" }, cancellationToken);
        var instances = new List<HyperionInstanceInfo>();
        foreach (var instance in reply.GetProperty("info").GetProperty("instance").EnumerateArray())
        {
            instances.Add(new HyperionInstanceInfo(
                instance.GetProperty("instance").GetInt32(),
                instance.GetProperty("friendly_name").GetString() ?? string.Empty,
                instance.GetProperty("running").GetBoolean()));
        }

        return instances;
    }

    /// <summary>Returns the priority list of a running instance (<c>serverinfo</c> → <c>priorities</c>).</summary>
    public async Task<IReadOnlyList<HyperionPriorityInfo>> GetPrioritiesAsync(int instance, CancellationToken cancellationToken)
    {
        var info = await GetServerInfoAsync(instance, cancellationToken);
        var priorities = new List<HyperionPriorityInfo>();
        foreach (var priority in info.GetProperty("priorities").EnumerateArray())
        {
            priorities.Add(new HyperionPriorityInfo(
                priority.GetProperty("priority").GetInt32(),
                priority.GetProperty("componentId").GetString() ?? string.Empty,
                priority.TryGetProperty("origin", out var origin) ? origin.GetString() ?? string.Empty : string.Empty,
                priority.GetProperty("active").GetBoolean(),
                priority.GetProperty("visible").GetBoolean()));
        }

        return priorities;
    }

    /// <summary>
    /// Returns the colors the instance sends to its LED device next (after smoothing and calibration), or an empty list
    /// when it did not update its LEDs within a second (nothing to show, for example after its last source was cleared).
    /// </summary>
    public async Task<IReadOnlyList<Rgb>> GetLedColorsAsync(int instance, CancellationToken cancellationToken)
    {
        JsonElement reply;
        try
        {
            reply = await SendAsync(
                new JsonObject { ["command"] = "instance-data", ["subcommand"] = "getLedSnapshot", ["instance"] = instance },
                cancellationToken);
        }
        catch (HyperionJsonRpcException ex) when (ex.Message.Contains("No LED color data available", StringComparison.Ordinal))
        {
            return [];
        }

        var colors = new List<Rgb>();
        foreach (var led in reply.GetProperty("info").GetProperty("leds").EnumerateArray())
        {
            colors.Add(new Rgb((byte)led[0].GetInt32(), (byte)led[1].GetInt32(), (byte)led[2].GetInt32()));
        }

        return colors;
    }

    /// <summary>Logs in as the admin user, which configuration and instance management require.</summary>
    public Task LoginAsync(string password, CancellationToken cancellationToken)
        => SendAsync(new JsonObject { ["command"] = "authorize", ["subcommand"] = "login", ["password"] = password }, cancellationToken);

    /// <summary>Returns the stored configuration: <c>{ global: { settings }, instances: [ { id, name, settings } ] }</c>.</summary>
    public async Task<JsonElement> GetConfigurationAsync(CancellationToken cancellationToken)
    {
        var reply = await SendAsync(new JsonObject { ["command"] = "config", ["subcommand"] = "getconfig" }, cancellationToken);
        return reply.GetProperty("info");
    }

    /// <summary>
    /// Clears a priority on one instance, like the web UI's remote control page, Home Assistant or hyperion-remote. A
    /// FlatBuffers client at that priority stays connected; only the instance forgets the priority.
    /// </summary>
    /// <remarks>Unlike <c>serverinfo</c>, <c>clear</c> takes a list of instances (<c>schema-clear.json</c>).</remarks>
    public Task ClearAsync(int instance, int priority, CancellationToken cancellationToken)
        => SendAsync(
            new JsonObject { ["command"] = "clear", ["priority"] = priority, ["instance"] = new JsonArray(instance) },
            cancellationToken);

    /// <summary>Saves settings of one instance (keys are setting types such as <c>leds</c> or <c>device</c>).</summary>
    public Task SaveInstanceSettingsAsync(int instance, JsonObject settings, CancellationToken cancellationToken)
        => SendAsync(
            new JsonObject
            {
                ["command"] = "config",
                ["subcommand"] = "setconfig",
                ["config"] = new JsonObject
                {
                    ["instances"] = new JsonArray(new JsonObject { ["id"] = instance, ["settings"] = settings }),
                },
            },
            cancellationToken);

    public Task CreateInstanceAsync(string name, CancellationToken cancellationToken)
        => SendAsync(new JsonObject { ["command"] = "instance", ["subcommand"] = "createInstance", ["name"] = name }, cancellationToken);

    /// <summary>Starts an instance; Hyperion replies once it runs, or with an error detail if it already does.</summary>
    public Task StartInstanceAsync(int instance, CancellationToken cancellationToken)
        => SendAsync(new JsonObject { ["command"] = "instance", ["subcommand"] = "startInstance", ["instance"] = instance }, cancellationToken);

    /// <summary>Asks an instance to stop; Hyperion replies before it has stopped.</summary>
    public Task StopInstanceAsync(int instance, CancellationToken cancellationToken)
        => SendAsync(new JsonObject { ["command"] = "instance", ["subcommand"] = "stopInstance", ["instance"] = instance }, cancellationToken);

    public ValueTask DisposeAsync()
    {
        _reader.Dispose();
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}

/// <summary>One entry of <c>serverinfo</c> → <c>instance</c>.</summary>
public sealed record HyperionInstanceInfo(int Id, string Name, bool Running);

/// <summary>One entry of <c>serverinfo</c> → <c>priorities</c>.</summary>
public sealed record HyperionPriorityInfo(int Priority, string ComponentId, string Origin, bool Active, bool Visible);

/// <summary>Hyperion answered a JSON-RPC request with <c>success: false</c>.</summary>
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
