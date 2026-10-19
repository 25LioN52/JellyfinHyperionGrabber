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
/// Hyperion.ng 2.2 (<c>libsrc/api/JsonAPI.cpp</c>, <c>JsonInfo.cpp</c>): a request picks its instance with an
/// <c>instance</c> field; <c>instance-data</c> / <c>getLedSnapshot</c> returns the colors an instance sends to its LED
/// device next (after smoothing and calibration); <c>config</c> reads and writes the settings of every instance,
/// running or not, as <c>{ global: { settings }, instances: [ { id, name, settings } ] }</c>.
/// </summary>
public sealed class HyperionNgApi(HyperionJsonRpc rpc) : HyperionServerApi(rpc)
{
    public override async Task<IReadOnlyList<Rgb>> GetLedColorsAsync(int instance, CancellationToken cancellationToken)
    {
        // Hyperion.ng answers "No LED color data available" when the instance did not update its LEDs within a
        // second, which it stops doing when it has nothing to show.
        JsonElement reply;
        try
        {
            reply = await SendToInstanceAsync(
                instance,
                new JsonObject { ["command"] = "instance-data", ["subcommand"] = "getLedSnapshot" },
                cancellationToken);
        }
        catch (HyperionJsonRpcException ex) when (ex.Message.Contains("No LED color data available", StringComparison.Ordinal))
        {
            return [];
        }

        return reply.GetProperty("info").GetProperty("leds").EnumerateArray()
            .Select(led => new Rgb((byte)led[0].GetInt32(), (byte)led[1].GetInt32(), (byte)led[2].GetInt32()))
            .ToList();
    }

    // Unlike the other commands, clear takes a list of instances (schema-clear.json).
    public override Task ClearAsync(int instance, int priority, CancellationToken cancellationToken)
        => SendGlobalAsync(
            new JsonObject { ["command"] = "clear", ["priority"] = priority, ["instance"] = new JsonArray(instance) },
            cancellationToken);

    public override async Task<IReadOnlyList<HyperionLedDeviceInfo>> GetLedDevicesAsync(CancellationToken cancellationToken)
    {
        var reply = await SendGlobalAsync(new JsonObject { ["command"] = "config", ["subcommand"] = "getconfig" }, cancellationToken);
        return reply.GetProperty("info").GetProperty("instances").EnumerateArray()
            .Select(instance => new HyperionLedDeviceInfo(instance.GetProperty("id").GetInt32(), DeviceType(instance.GetProperty("settings"))))
            .ToList();
    }

    public override Task ConfigureInstanceAsync(int instance, IReadOnlyList<LedRegion> regions, CancellationToken cancellationToken)
    {
        var settings = new JsonObject
        {
            ["leds"] = LedLayout(regions),
            ["device"] = new JsonObject
            {
                ["type"] = "file",
                ["output"] = "/dev/null",
                ["hardwareLedCount"] = regions.Count,
                ["autoStart"] = true,
                ["colorOrder"] = "rgb",
                ["latchTime"] = 0,
                ["rewriteTime"] = 0,
                ["enableAttempts"] = 6,
                ["enableAttemptsInterval"] = 15,
            },
            ["foregroundEffect"] = new JsonObject
            {
                ["enable"] = false,
                ["type"] = "effect",
                ["color"] = new JsonArray(0, 0, 255),
                ["effect"] = "Rainbow swirl fast",
                ["duration_ms"] = 3000,
            },
        };

        return SendGlobalAsync(
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
    }

    protected override Task<JsonElement> SendGlobalAsync(JsonObject request, CancellationToken cancellationToken)
        => Rpc.SendAsync(request, cancellationToken);

    protected override Task<JsonElement> SendToInstanceAsync(int instance, JsonObject request, CancellationToken cancellationToken)
    {
        request["instance"] = instance;
        return Rpc.SendAsync(request, cancellationToken);
    }

    private static string? DeviceType(JsonElement settings)
        => settings.TryGetProperty("device", out var device) && device.TryGetProperty("type", out var type) ? type.GetString() : null;
}
