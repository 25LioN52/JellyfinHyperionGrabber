using System;
using Jellyfin.Plugin.HyperionGrabber.Core.Playback;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.HyperionGrabber.Playback;

/// <summary>
/// <see cref="IPlaybackFilterSource"/> backed by the plugin's saved configuration.
/// </summary>
internal sealed class PluginPlaybackFilterSource : IPlaybackFilterSource, IDisposable
{
    private readonly Plugin? _plugin;

    /// <summary>
    /// Initializes a new instance of the <see cref="PluginPlaybackFilterSource"/> class.
    /// </summary>
    public PluginPlaybackFilterSource()
    {
        // Jellyfin creates plugin instances before it builds the service provider, but does not register them in it.
        _plugin = Plugin.Instance;
        if (_plugin is not null)
        {
            _plugin.ConfigurationChanged += OnConfigurationChanged;
        }
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public PlaybackFilter GetCurrent() => _plugin?.Configuration?.ToPlaybackFilter() ?? PlaybackFilter.Disabled;

    /// <inheritdoc />
    public void Dispose()
    {
        if (_plugin is not null)
        {
            _plugin.ConfigurationChanged -= OnConfigurationChanged;
        }
    }

    private void OnConfigurationChanged(object? sender, BasePluginConfiguration configuration) => Changed?.Invoke(this, EventArgs.Empty);
}
