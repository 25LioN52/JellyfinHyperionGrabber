using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Diagnostics;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.EndToEnd;

/// <summary>
/// HyperHDR 22 (<c>sources/api/HyperAPI.cpp</c>, <c>BaseAPI.cpp</c>, <c>JSONRPC_schema/</c>). Where it differs from
/// Hyperion.ng:
/// <list type="bullet">
/// <item>A connection works on one instance at a time, chosen with <c>instance</c> / <c>switchTo</c>; requests have
/// no <c>instance</c> field (the schemas reject unknown fields). While the connection's instance is not running, the
/// server answers every command, <c>switchTo</c> included, with "Not ready", so this class switches only to running
/// instances and sends everything else from instance 0.</item>
/// <item>There is no LED snapshot: <c>ledcolors</c> / <c>ledstream-start</c> streams the colors the instance computed
/// for its LEDs (before smoothing) as one flat <c>[r, g, b, r, g, b, ...]</c> array.</item>
/// <item><c>config</c> reads and writes the settings of the connection's instance only, and only while it runs.
/// <c>setconfig</c> checks what it gets against the full schema, so settings are changed by reading all of them and
/// writing them back, as HyperHDR's web UI does.</item>
/// </list>
/// </summary>
public sealed class HyperHdrApi(HyperionJsonRpc rpc) : HyperionServerApi(rpc)
{
    private const int FirstInstance = 0;

    /// <summary>The instance this connection works on; HyperHDR starts every connection on instance 0.</summary>
    private int _current = FirstInstance;

    public override async Task<IReadOnlyList<Rgb>> GetLedColorsAsync(int instance, CancellationToken cancellationToken)
    {
        var start = await SendToInstanceAsync(
            instance,
            new JsonObject { ["command"] = "ledcolors", ["subcommand"] = "ledstream-start", ["interval"] = 50 },
            cancellationToken);

        // Updates repeat the tan of the request that started the stream. The connection keeps the colors it last
        // received across switchTo, so an update right after a switch can still show the previous instance; callers
        // read until the colors match (Eventually), which also covers that.
        var tan = start.GetProperty("tan").GetInt32();
        var update = await Rpc.ReadAsync(
            message => HyperionJsonRpc.HasTan(message, tan)
                && message.TryGetProperty("command", out var command)
                && command.GetString() == "ledcolors-ledstream-update",
            cancellationToken);
        await Rpc.SendAsync(new JsonObject { ["command"] = "ledcolors", ["subcommand"] = "ledstream-stop" }, cancellationToken);

        var channels = update.GetProperty("result").GetProperty("leds").EnumerateArray().Select(channel => (byte)channel.GetInt32()).ToArray();
        return channels.Chunk(3).Where(led => led.Length == 3).Select(led => new Rgb(led[0], led[1], led[2])).ToList();
    }

    public override Task ClearAsync(int instance, int priority, CancellationToken cancellationToken)
        => SendToInstanceAsync(instance, new JsonObject { ["command"] = "clear", ["priority"] = priority }, cancellationToken);

    public override async Task<IReadOnlyList<HyperionLedDeviceInfo>> GetLedDevicesAsync(CancellationToken cancellationToken)
    {
        var devices = new List<HyperionLedDeviceInfo>();
        foreach (var instance in await GetInstancesAsync(cancellationToken))
        {
            // HyperHDR shows the settings of running instances only.
            var type = instance.Running ? DeviceType(await GetSettingsAsync(instance.Id, cancellationToken)) : null;
            devices.Add(new HyperionLedDeviceInfo(instance.Id, type));
        }

        return devices;
    }

    public override async Task ConfigureInstanceAsync(int instance, IReadOnlyList<LedRegion> regions, CancellationToken cancellationToken)
    {
        if (!(await GetInstancesAsync(cancellationToken)).Single(info => info.Id == instance).Running)
        {
            await StartInstanceAsync(instance, cancellationToken);
        }

        var settings = await GetSettingsAsync(instance, cancellationToken);
        var type = DeviceType(settings);
        if (type != "file")
        {
            throw new InvalidOperationException(
                $"HyperHDR instance {instance} drives a '{type}' LED device; the end-to-end tests only overwrite the 'file' device.");
        }

        var leds = LedLayout(regions);
        foreach (var led in leds)
        {
            led!["group"] = 0;
        }

        settings["leds"] = leds;
        var device = settings["device"]!.AsObject();
        device["output"] = "/dev/null";
        device["hardwareLedCount"] = regions.Count;

        // HyperHDR 22 returned the settings of a newly created instance without foregroundEffect; setconfig fills in
        // the defaults of anything missing (its log shows them as "Config Fix").
        if (settings["foregroundEffect"] is not JsonObject effect)
        {
            effect = new JsonObject();
            settings["foregroundEffect"] = effect;
        }

        effect["enable"] = false;

        await SendToInstanceAsync(
            instance,
            new JsonObject { ["command"] = "config", ["subcommand"] = "setconfig", ["config"] = settings },
            cancellationToken);
    }

    protected override Task<JsonElement> SendGlobalAsync(JsonObject request, CancellationToken cancellationToken)
        => SendToInstanceAsync(FirstInstance, request, cancellationToken);

    protected override async Task<JsonElement> SendToInstanceAsync(int instance, JsonObject request, CancellationToken cancellationToken)
    {
        if (instance != _current)
        {
            await Rpc.SendAsync(
                new JsonObject { ["command"] = "instance", ["subcommand"] = "switchTo", ["instance"] = instance },
                cancellationToken);
            _current = instance;
        }

        return await Rpc.SendAsync(request, cancellationToken);
    }

    private static string? DeviceType(JsonObject settings) => (string?)settings["device"]?["type"];

    private async Task<JsonObject> GetSettingsAsync(int instance, CancellationToken cancellationToken)
    {
        var reply = await SendToInstanceAsync(instance, new JsonObject { ["command"] = "config", ["subcommand"] = "getconfig" }, cancellationToken);
        return JsonNode.Parse(reply.GetProperty("info").GetRawText())!.AsObject();
    }
}
