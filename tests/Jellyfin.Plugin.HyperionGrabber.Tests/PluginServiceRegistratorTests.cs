using System.Linq;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;
using Jellyfin.Plugin.HyperionGrabber.Playback;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Session;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Tests;

public class PluginServiceRegistratorTests
{
    [Fact]
    public void RegisterServices_PlaybackMonitorResolvesFromJellyfinServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(Substitute.For<ISessionManager>());

        new PluginServiceRegistrator().RegisterServices(services, Substitute.For<IServerApplicationHost>());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        Assert.Single(provider.GetServices<IHostedService>().OfType<PlaybackMonitorService>());
        Assert.IsType<LoggingGrabSessionFactory>(provider.GetRequiredService<IGrabSessionFactory>());
    }
}
