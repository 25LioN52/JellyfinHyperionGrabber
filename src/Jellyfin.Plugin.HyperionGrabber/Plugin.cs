using System;
using System.Collections.Generic;
using Jellyfin.Plugin.HyperionGrabber.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.HyperionGrabber;

/// <summary>
/// Plugin entry point discovered by Jellyfin.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Stable plugin identifier. Must match build/plugin.json and Configuration/configPage.js.
    /// </summary>
    public const string PluginIdString = "501879a2-6653-4450-b66c-73ba37aa6e3f";

    /// <summary>Display name in the Jellyfin dashboard and plugin catalog.</summary>
    public const string PluginName = "Hyperion Grabber";

    /// <summary>Name of the configuration page (used in dashboard URLs).</summary>
    internal const string ConfigPageName = "HyperionGrabber";

    /// <summary>Name of the configuration page script, referenced by the page's data-controller.</summary>
    internal const string ConfigScriptName = "HyperionGrabberJs";

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Jellyfin application paths.</param>
    /// <param name="xmlSerializer">Serializer for the plugin configuration file.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
    }

    /// <inheritdoc />
    public override string Name => PluginName;

    /// <inheritdoc />
    public override Guid Id => Guid.Parse(PluginIdString);

    /// <inheritdoc />
    public override string Description => "Ambient lighting for Jellyfin: streams the video you are watching to Hyperion.ng or HyperHDR.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        var resourcePrefix = GetType().Namespace + ".Configuration.";
        return
        [
            new PluginPageInfo
            {
                Name = ConfigPageName,
                EmbeddedResourcePath = resourcePrefix + "configPage.html",
            },
            new PluginPageInfo
            {
                Name = ConfigScriptName,
                EmbeddedResourcePath = resourcePrefix + "configPage.js",
            },
        ];
    }
}
