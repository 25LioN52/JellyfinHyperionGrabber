using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.EndToEnd;

/// <summary>
/// A real Hyperion.ng for the end-to-end tests, prepared once per test run over its JSON-RPC API.
/// </summary>
/// <remarks>
/// <para>Set <c>HYPERION_GRABBER_E2E_HOST</c> to run the tests; without it they are skipped. CI starts
/// <c>tests/e2e/hyperion-ng</c> (the official APT package in Docker) and sets it (docs/development/testing.md).</para>
/// <para>The fixture <b>reconfigures</b> the server: it gives instance 0 four LEDs that sample the middle of each
/// picture edge (top, right, bottom, left) and creates a second instance whose four LEDs sample the same regions in
/// reverse order. Each instance's LED colors therefore show whether a picture arrived, and the right way round. To
/// protect a real installation it refuses to touch a Hyperion with any LED device other than <c>file</c>.</para>
/// </remarks>
public sealed class HyperionNgFixture : IAsyncLifetime
{
    public const string HostVariable = "HYPERION_GRABBER_E2E_HOST";
    public const int FirstInstance = 0;
    public const string SecondInstanceName = "Grabber E2E second instance";

    /// <summary>LED regions in instance 0's strip order: the middle of the top, right, bottom and left edge.</summary>
    public static readonly IReadOnlyList<LedRegion> EdgeRegions =
    [
        new(0.40, 0.60, 0.00, 0.10),
        new(0.90, 1.00, 0.40, 0.60),
        new(0.40, 0.60, 0.90, 1.00),
        new(0.00, 0.10, 0.40, 0.60),
    ];

    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(120);

    public HyperionNgFixture()
    {
        var host = Environment.GetEnvironmentVariable(HostVariable);
        Host = string.IsNullOrWhiteSpace(host) ? null : host.Trim();
        FlatBuffersPort = PortFromEnvironment("HYPERION_GRABBER_E2E_FLATBUFFERS_PORT", HyperionDefaults.FlatBuffersPort);
        JsonPort = PortFromEnvironment("HYPERION_GRABBER_E2E_JSON_PORT", 19444);
    }

    public string? Host { get; }

    public int FlatBuffersPort { get; }

    public int JsonPort { get; }

    /// <summary>Gets the id of the second instance, whose LEDs sample <see cref="EdgeRegions"/> in reverse order.</summary>
    public int SecondInstance { get; private set; } = -1;

    public HyperionClientOptions ClientOptions => new()
    {
        Host = Host ?? throw new InvalidOperationException($"{HostVariable} is not set."),
        Port = FlatBuffersPort,
        Origin = "Grabber E2E",
    };

    public void SkipIfUnavailable() => Assert.SkipWhen(
        Host is null,
        $"Set {HostVariable} to run the end-to-end tests against a throwaway Hyperion.ng (see docs/development/testing.md).");

    public Task<HyperionJsonRpc> ConnectApiAsync(CancellationToken cancellationToken)
        => HyperionJsonRpc.ConnectAsync(Host!, JsonPort, cancellationToken);

    public async ValueTask InitializeAsync()
    {
        if (Host is null)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(StartupTimeout);
        var ct = timeout.Token;
        await using var api = await ConnectWhenReadyAsync(ct);
        await api.LoginAsync(Environment.GetEnvironmentVariable("HYPERION_GRABBER_E2E_PASSWORD") ?? "hyperion", ct);
        EnsureThrowawayServer(await api.GetConfigurationAsync(ct));

        await api.SaveInstanceSettingsAsync(FirstInstance, InstanceSettings(EdgeRegions), ct);

        var second = (await api.GetInstancesAsync(ct)).FirstOrDefault(instance => instance.Name == SecondInstanceName);
        if (second is null)
        {
            await api.CreateInstanceAsync(SecondInstanceName, ct);
            second = (await api.GetInstancesAsync(ct)).Single(instance => instance.Name == SecondInstanceName);
        }

        SecondInstance = second.Id;
        await api.SaveInstanceSettingsAsync(SecondInstance, InstanceSettings(EdgeRegions.Reverse().ToList()), ct);

        // Instance 0 plays Hyperion's 3 s startup effect at a higher priority than ours; let it finish.
        await Eventually.ReachAsync(
            () => api.GetPrioritiesAsync(FirstInstance, ct),
            priorities => !priorities.Any(priority => priority.ComponentId == "EFFECT"),
            StartupTimeout,
            priorities => "the startup effect is still running: " + string.Join(", ", priorities));
    }

    /// <summary>Starts or stops the second instance and waits until Hyperion reports the new state.</summary>
    public async Task SetSecondInstanceRunningAsync(HyperionJsonRpc api, bool running, CancellationToken cancellationToken)
    {
        var current = (await api.GetInstancesAsync(cancellationToken)).Single(instance => instance.Id == SecondInstance);
        if (current.Running != running)
        {
            await (running ? api.StartInstanceAsync(SecondInstance, cancellationToken) : api.StopInstanceAsync(SecondInstance, cancellationToken));
        }

        await Eventually.ReachAsync(
            () => api.GetInstancesAsync(cancellationToken),
            instances => instances.Single(instance => instance.Id == SecondInstance).Running == running,
            TimeSpan.FromSeconds(15),
            instances => $"instance {SecondInstance} should be {(running ? "running" : "stopped")}: " + string.Join(", ", instances));
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static int PortFromEnvironment(string variable, int fallback)
        => int.TryParse(Environment.GetEnvironmentVariable(variable), NumberStyles.None, CultureInfo.InvariantCulture, out var port) ? port : fallback;

    /// <summary>Settings for one instance: the LED regions, a <c>file</c> device with that many LEDs, no startup effect.</summary>
    private static JsonObject InstanceSettings(IReadOnlyList<LedRegion> regions) => new()
    {
        ["leds"] = new JsonArray(regions.Select(region => (JsonNode)new JsonObject
        {
            ["hmin"] = region.HMin,
            ["hmax"] = region.HMax,
            ["vmin"] = region.VMin,
            ["vmax"] = region.VMax,
        }).ToArray()),
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

    /// <summary>Refuses to reconfigure a Hyperion that drives real LEDs.</summary>
    private void EnsureThrowawayServer(JsonElement configuration)
    {
        foreach (var instance in configuration.GetProperty("instances").EnumerateArray())
        {
            var type = instance.GetProperty("settings").TryGetProperty("device", out var device) && device.TryGetProperty("type", out var deviceType)
                ? deviceType.GetString()
                : null;
            if (type != "file")
            {
                throw new InvalidOperationException(
                    $"Hyperion at {Host}:{JsonPort} drives a '{type}' LED device on instance {instance.GetProperty("id").GetInt32()}. "
                    + "The end-to-end tests overwrite the LED layout and device, so they only run against a throwaway Hyperion "
                    + "whose instances all use the 'file' device, such as the tests/e2e/hyperion-ng container.");
            }
        }
    }

    private async Task<HyperionJsonRpc> ConnectWhenReadyAsync(CancellationToken cancellationToken)
    {
        // The container may still be starting: retry until the JSON-RPC server answers.
        while (true)
        {
            HyperionJsonRpc? api = null;
            try
            {
                api = await ConnectApiAsync(cancellationToken);
                await api.GetInstancesAsync(cancellationToken);
                return api;
            }
            catch (Exception ex) when ((ex is SocketException or IOException or HyperionJsonRpcException or OperationCanceledException) && !cancellationToken.IsCancellationRequested)
            {
                if (api is not null)
                {
                    await api.DisposeAsync();
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
    }
}

/// <summary>A picture region an LED follows, as fractions of the width (h) and height (v).</summary>
public sealed record LedRegion(double HMin, double HMax, double VMin, double VMax);

[CollectionDefinition(Name)]
public sealed class SharedHyperionNg : ICollectionFixture<HyperionNgFixture>
{
    /// <summary>All end-to-end tests share one server, so they run one after another.</summary>
    public const string Name = "Hyperion.ng";
}
