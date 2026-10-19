using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.HyperionGrabber.Core.Hyperion;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Core.Tests.EndToEnd;

/// <summary>
/// A real Hyperion.ng or HyperHDR for the end-to-end tests, prepared once per test run over its JSON-RPC API.
/// </summary>
/// <remarks>
/// <para>Set <c>HYPERION_GRABBER_E2E_HOST</c> to run the tests; without it they are skipped. CI starts
/// <c>tests/e2e/hyperion-ng</c> and <c>tests/e2e/hyperhdr</c> (the official packages in Docker) and sets it
/// (docs/development/testing.md). The fixture asks the server which of the two it is.</para>
/// <para>The fixture <b>reconfigures</b> the server: it gives instance 0 four LEDs that sample the middle of each
/// picture edge (top, right, bottom, left) and creates a second instance whose four LEDs sample the same regions in
/// reverse order. Each instance's LED colors therefore show whether a picture arrived, and the right way round. To
/// protect a real installation it refuses to touch a server with any LED device other than <c>file</c>.</para>
/// </remarks>
public sealed class HyperionServerFixture : IAsyncLifetime
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

    public HyperionServerFixture()
    {
        var host = Environment.GetEnvironmentVariable(HostVariable);
        Host = string.IsNullOrWhiteSpace(host) ? null : host.Trim();
        FlatBuffersPort = PortFromEnvironment("HYPERION_GRABBER_E2E_FLATBUFFERS_PORT", HyperionDefaults.FlatBuffersPort);
        JsonPort = PortFromEnvironment("HYPERION_GRABBER_E2E_JSON_PORT", 19444);
    }

    public string? Host { get; }

    public int FlatBuffersPort { get; }

    public int JsonPort { get; }

    /// <summary>Gets the server under test, as it describes itself.</summary>
    public HyperionServerInfo? Server { get; private set; }

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
        $"Set {HostVariable} to run the end-to-end tests against a throwaway Hyperion.ng or HyperHDR (see docs/development/testing.md).");

    public Task<HyperionServerApi> ConnectApiAsync(CancellationToken cancellationToken)
        => HyperionServerApi.ConnectAsync(Server!.Kind, Host!, JsonPort, cancellationToken);

    public async ValueTask InitializeAsync()
    {
        if (Host is null)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(StartupTimeout);
        var ct = timeout.Token;
        await using var api = await ConnectWhenReadyAsync(ct);
        await api.LoginAsync(Environment.GetEnvironmentVariable("HYPERION_GRABBER_E2E_PASSWORD") ?? DefaultPassword(Server!.Kind), ct);
        var instances = await api.GetInstancesAsync(ct);
        EnsureThrowawayServer(await api.GetLedDevicesAsync(ct), instances);

        await api.ConfigureInstanceAsync(FirstInstance, EdgeRegions, ct);

        var second = instances.FirstOrDefault(instance => instance.Name == SecondInstanceName);
        if (second is null)
        {
            await api.CreateInstanceAsync(SecondInstanceName, ct);
            second = (await api.GetInstancesAsync(ct)).Single(instance => instance.Name == SecondInstanceName);
        }

        SecondInstance = second.Id;
        await api.ConfigureInstanceAsync(SecondInstance, EdgeRegions.Reverse().ToList(), ct);

        // An instance plays a 3 s startup effect at a higher priority than ours when it starts; let it finish.
        foreach (var instance in (await api.GetInstancesAsync(ct)).Where(instance => instance.Running))
        {
            await Eventually.ReachAsync(
                () => api.GetPrioritiesAsync(instance.Id, ct),
                priorities => !priorities.Any(priority => priority.ComponentId == "EFFECT"),
                StartupTimeout,
                priorities => $"the startup effect of instance {instance.Id} is still running: " + string.Join(", ", priorities));
        }
    }

    /// <summary>Starts or stops the second instance and waits until the server reports the new state.</summary>
    public async Task SetSecondInstanceRunningAsync(HyperionServerApi api, bool running, CancellationToken cancellationToken)
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

    /// <summary>The admin password of a fresh installation.</summary>
    private static string DefaultPassword(HyperionServerKind kind) => kind == HyperionServerKind.HyperHdr ? "hyperhdr" : "hyperion";

    /// <summary>Refuses to reconfigure a server that drives real LEDs.</summary>
    private void EnsureThrowawayServer(IReadOnlyList<HyperionLedDeviceInfo> devices, IReadOnlyList<HyperionInstanceInfo> instances)
    {
        foreach (var device in devices)
        {
            // HyperHDR does not show the settings of a stopped instance. Our own second instance is checked when it is
            // started to be configured (HyperHdrApi.ConfigureInstanceAsync); any other is refused.
            var ours = instances.Any(instance => instance.Id == device.Instance && instance.Name == SecondInstanceName);
            if (device.Type == "file" || (device.Type is null && ours))
            {
                continue;
            }

            throw new InvalidOperationException(
                $"{Server!.Version} at {Host}:{JsonPort} drives a '{device.Type ?? "unknown (stopped instance)"}' LED device on instance {device.Instance}. "
                + "The end-to-end tests overwrite the LED layout and device, so they only run against a throwaway server "
                + "whose instances all use the 'file' device, such as the tests/e2e/hyperion-ng and tests/e2e/hyperhdr containers.");
        }
    }

    private async Task<HyperionServerApi> ConnectWhenReadyAsync(CancellationToken cancellationToken)
    {
        // The container may still be starting: retry until the JSON-RPC server answers.
        while (true)
        {
            HyperionJsonRpc? rpc = null;
            try
            {
                rpc = await HyperionJsonRpc.ConnectAsync(Host!, JsonPort, cancellationToken);
                Server = await HyperionServerApi.IdentifyAsync(rpc, cancellationToken);
                var api = HyperionServerApi.Create(Server.Kind, rpc);
                await api.GetInstancesAsync(cancellationToken);
                return api;
            }
            catch (Exception ex) when ((ex is SocketException or IOException or HyperionJsonRpcException or OperationCanceledException) && !cancellationToken.IsCancellationRequested)
            {
                if (rpc is not null)
                {
                    await rpc.DisposeAsync();
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
    }
}

[CollectionDefinition(Name)]
public sealed class SharedHyperionServer : ICollectionFixture<HyperionServerFixture>
{
    /// <summary>All end-to-end tests share one server, so they run one after another.</summary>
    public const string Name = "Hyperion server";
}
