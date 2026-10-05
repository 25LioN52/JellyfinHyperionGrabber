using System.Linq;
using Jellyfin.Plugin.HyperionGrabber.Core.Diagnostics;
using Jellyfin.Plugin.HyperionGrabber.Core.Frames;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;
using Jellyfin.Plugin.HyperionGrabber.Frames;
using Jellyfin.Plugin.HyperionGrabber.Playback;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.MediaEncoding;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Tests;

/// <summary>
/// Builds the container the way Jellyfin does, with every Jellyfin service the plugin depends on. A new dependency
/// on a Jellyfin service must be added here, which keeps the list of host requirements in one reviewed place.
/// </summary>
public class PluginServiceRegistratorTests
{
    [Fact]
    public void RegisterServices_AllPluginServicesResolveFromJellyfinServices()
    {
        using var provider = BuildProvider();

        Assert.Single(provider.GetServices<IHostedService>().OfType<PlaybackMonitorService>());
        Assert.IsType<LoggingGrabSessionFactory>(provider.GetRequiredService<IGrabSessionFactory>());
        Assert.IsType<JellyfinFfmpegSettingsProvider>(provider.GetRequiredService<IFfmpegSettingsProvider>());
        Assert.NotNull(provider.GetRequiredService<HyperionConnectionTester>());
    }

    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // Jellyfin services the plugin uses.
        services.AddSingleton(Substitute.For<ISessionManager>());
        services.AddSingleton(Substitute.For<IMediaEncoder>());
        services.AddSingleton(Substitute.For<IServerConfigurationManager>());

        new PluginServiceRegistrator().RegisterServices(services, Substitute.For<IServerApplicationHost>());
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
