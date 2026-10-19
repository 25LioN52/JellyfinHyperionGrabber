using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Diagnostics;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.EndToEnd;

/// <summary>The servers the plugin supports.</summary>
public enum HyperionServerKind
{
    HyperionNg,
    HyperHdr,
}

/// <summary>
/// What the end-to-end tests read from and change in a real Hyperion.ng or HyperHDR over its JSON-RPC API. HyperHDR
/// grew out of Hyperion.ng and shares most commands and reply shapes; <see cref="HyperionNgApi"/> and
/// <see cref="HyperHdrApi"/> cover where they differ: how a request picks its instance, how LED colors are read and
/// how settings are stored.
/// </summary>
public abstract class HyperionServerApi(HyperionJsonRpc rpc) : IAsyncDisposable
{
    protected HyperionJsonRpc Rpc { get; } = rpc;

    /// <summary>Opens a connection to a server whose kind is known (see <see cref="IdentifyAsync"/>).</summary>
    public static async Task<HyperionServerApi> ConnectAsync(HyperionServerKind kind, string host, int port, CancellationToken cancellationToken)
        => Create(kind, await HyperionJsonRpc.ConnectAsync(host, port, cancellationToken));

    public static HyperionServerApi Create(HyperionServerKind kind, HyperionJsonRpc rpc) => kind switch
    {
        HyperionServerKind.HyperHdr => new HyperHdrApi(rpc),
        _ => new HyperionNgApi(rpc),
    };

    /// <summary>Asks a server what it is: <c>sysinfo</c> describes it as <c>hyperion</c> or <c>hyperhdr</c>.</summary>
    public static async Task<HyperionServerInfo> IdentifyAsync(HyperionJsonRpc rpc, CancellationToken cancellationToken)
    {
        var info = (await rpc.SendAsync(new JsonObject { ["command"] = "sysinfo" }, cancellationToken)).GetProperty("info");
        if (info.TryGetProperty("hyperhdr", out var hyperhdr))
        {
            return new(HyperionServerKind.HyperHdr, "HyperHDR " + hyperhdr.GetProperty("version").GetString());
        }

        if (info.TryGetProperty("hyperion", out var hyperion))
        {
            return new(HyperionServerKind.HyperionNg, "Hyperion.ng " + hyperion.GetProperty("version").GetString());
        }

        throw new HyperionJsonRpcException("The server is neither Hyperion.ng nor HyperHDR: " + info.GetRawText());
    }

    /// <summary>Logs in as the admin user, which configuration and instance management require.</summary>
    public Task LoginAsync(string password, CancellationToken cancellationToken)
        => SendGlobalAsync(new JsonObject { ["command"] = "authorize", ["subcommand"] = "login", ["password"] = password }, cancellationToken);

    /// <summary>Returns the instances the server knows, running or not (<c>serverinfo</c> → <c>instance</c>).</summary>
    public async Task<IReadOnlyList<HyperionInstanceInfo>> GetInstancesAsync(CancellationToken cancellationToken)
    {
        var reply = await SendGlobalAsync(new JsonObject { ["command"] = "serverinfo" }, cancellationToken);
        return reply.GetProperty("info").GetProperty("instance").EnumerateArray()
            .Select(instance => new HyperionInstanceInfo(
                instance.GetProperty("instance").GetInt32(),
                instance.GetProperty("friendly_name").GetString() ?? string.Empty,
                instance.GetProperty("running").GetBoolean()))
            .ToList();
    }

    /// <summary>Returns the priority list of a running instance (<c>serverinfo</c> → <c>priorities</c>).</summary>
    public async Task<IReadOnlyList<HyperionPriorityInfo>> GetPrioritiesAsync(int instance, CancellationToken cancellationToken)
    {
        var reply = await SendToInstanceAsync(instance, new JsonObject { ["command"] = "serverinfo" }, cancellationToken);
        return reply.GetProperty("info").GetProperty("priorities").EnumerateArray()
            .Select(priority => new HyperionPriorityInfo(
                priority.GetProperty("priority").GetInt32(),
                priority.GetProperty("componentId").GetString() ?? string.Empty,
                priority.TryGetProperty("origin", out var origin) ? origin.GetString() ?? string.Empty : string.Empty,
                priority.GetProperty("active").GetBoolean(),
                priority.GetProperty("visible").GetBoolean()))
            .ToList();
    }

    /// <summary>Returns the colors a running instance computed for its LEDs from the current picture.</summary>
    public abstract Task<IReadOnlyList<Rgb>> GetLedColorsAsync(int instance, CancellationToken cancellationToken);

    /// <summary>Returns the LED device type of every instance, as far as the server shows it.</summary>
    public abstract Task<IReadOnlyList<HyperionLedDeviceInfo>> GetLedDevicesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Gives an instance one LED per region on the <c>file</c> device (which writes to <c>/dev/null</c>) and turns
    /// off its startup effect.
    /// </summary>
    public abstract Task ConfigureInstanceAsync(int instance, IReadOnlyList<LedRegion> regions, CancellationToken cancellationToken);

    public Task CreateInstanceAsync(string name, CancellationToken cancellationToken)
        => SendGlobalAsync(new JsonObject { ["command"] = "instance", ["subcommand"] = "createInstance", ["name"] = name }, cancellationToken);

    /// <summary>Starts an instance; the server replies once it runs, or with an error if it already does.</summary>
    public Task StartInstanceAsync(int instance, CancellationToken cancellationToken)
        => SendGlobalAsync(new JsonObject { ["command"] = "instance", ["subcommand"] = "startInstance", ["instance"] = instance }, cancellationToken);

    /// <summary>Asks an instance to stop; the server replies before it has stopped.</summary>
    public Task StopInstanceAsync(int instance, CancellationToken cancellationToken)
        => SendGlobalAsync(new JsonObject { ["command"] = "instance", ["subcommand"] = "stopInstance", ["instance"] = instance }, cancellationToken);

    public ValueTask DisposeAsync() => Rpc.DisposeAsync();

    /// <summary>The <c>leds</c> setting: one LED per region, in order.</summary>
    protected static JsonArray LedLayout(IReadOnlyList<LedRegion> regions) => new(regions.Select(region => (JsonNode)new JsonObject
    {
        ["hmin"] = region.HMin,
        ["hmax"] = region.HMax,
        ["vmin"] = region.VMin,
        ["vmax"] = region.VMax,
    }).ToArray());

    /// <summary>Sends a request that is not about one instance (login, instance list and management).</summary>
    protected abstract Task<JsonElement> SendGlobalAsync(JsonObject request, CancellationToken cancellationToken);

    /// <summary>Sends a request that reads or changes one instance.</summary>
    protected abstract Task<JsonElement> SendToInstanceAsync(int instance, JsonObject request, CancellationToken cancellationToken);
}

/// <summary>The kind and version of the server under test, for example <c>HyperHDR 22.0.0</c>.</summary>
public sealed record HyperionServerInfo(HyperionServerKind Kind, string Version);

/// <summary>One entry of <c>serverinfo</c> → <c>instance</c>.</summary>
public sealed record HyperionInstanceInfo(int Id, string Name, bool Running);

/// <summary>One entry of <c>serverinfo</c> → <c>priorities</c>.</summary>
public sealed record HyperionPriorityInfo(int Priority, string ComponentId, string Origin, bool Active, bool Visible);

/// <summary>The LED device type of an instance; <see langword="null"/> when the server does not show it.</summary>
public sealed record HyperionLedDeviceInfo(int Instance, string? Type);

/// <summary>A picture region an LED follows, as fractions of the width (h) and height (v).</summary>
public sealed record LedRegion(double HMin, double HMax, double VMin, double VMax);
