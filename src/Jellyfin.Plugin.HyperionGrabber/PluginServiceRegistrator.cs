using System;
using Jellyfin.Plugin.HyperionGrabber.Core.Diagnostics;
using Jellyfin.Plugin.HyperionGrabber.Core.Frames;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;
using Jellyfin.Plugin.HyperionGrabber.Core.Streaming;
using Jellyfin.Plugin.HyperionGrabber.Frames;
using Jellyfin.Plugin.HyperionGrabber.Playback;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Jellyfin.Plugin.HyperionGrabber;

/// <summary>
/// Registers the plugin's services in Jellyfin's dependency injection container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.TryAddSingleton(TimeProvider.System);
        serviceCollection.AddSingleton<HyperionConnectionTester>();
        serviceCollection.AddSingleton<IFfmpegSettingsProvider, JellyfinFfmpegSettingsProvider>();
        serviceCollection.AddSingleton<IVideoInputResolver, JellyfinVideoInputResolver>();
        serviceCollection.AddSingleton<IStreamingSettingsProvider, PluginStreamingSettingsProvider>();
        serviceCollection.TryAddSingleton<IGrabSessionFactory, StreamingGrabSessionFactory>();
        serviceCollection.AddSingleton<IPlaybackFilterSource, PluginPlaybackFilterSource>();
        serviceCollection.AddHostedService<PlaybackMonitorService>();
    }
}
