using System;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Model.Serialization;
using NSubstitute;
using Xunit;

namespace Jellyfin.Plugin.HyperionGrabber.Tests;

/// <summary>
/// The plugin id, page names and element ids are duplicated across C#, HTML, JS and packaging metadata;
/// these tests keep them consistent.
/// </summary>
public class PluginTests
{
    [Fact]
    public void Id_MatchesPackagingMetadataAndConfigScript()
    {
        var plugin = CreatePlugin();
        using var metadata = JsonDocument.Parse(RepositoryFiles.ReadAllText("build/plugin.json"));

        Assert.Equal(Guid.Parse(Plugin.PluginIdString), plugin.Id);
        Assert.Equal(Plugin.PluginIdString, metadata.RootElement.GetProperty("guid").GetString());
        Assert.Equal(Plugin.PluginName, metadata.RootElement.GetProperty("name").GetString());
        Assert.Contains($"pluginUniqueId: '{Plugin.PluginIdString}'", RepositoryFiles.ReadEmbeddedResource("configPage.js"), StringComparison.Ordinal);
    }

    [Fact]
    public void PackagingMetadata_ListsTheShippedAssemblies()
    {
        using var metadata = JsonDocument.Parse(RepositoryFiles.ReadAllText("build/plugin.json"));
        var artifacts = metadata.RootElement.GetProperty("artifacts").EnumerateArray().Select(a => a.GetString()).ToArray();

        Assert.Contains(typeof(Plugin).Assembly.GetName().Name + ".dll", artifacts);
        Assert.Contains(typeof(Core.Hyperion.HyperionClient).Assembly.GetName().Name + ".dll", artifacts);
    }

    [Fact]
    public void GetPages_PointAtEmbeddedResources()
    {
        var pages = CreatePlugin().GetPages().ToList();

        Assert.Equal([Plugin.ConfigPageName, Plugin.ConfigScriptName], pages.Select(p => p.Name));
        Assert.All(pages, page => Assert.NotNull(RepositoryFiles.PluginAssembly.GetManifestResourceStream(page.EmbeddedResourcePath)));
    }

    [Fact]
    public void ConfigPage_LoadsItsScriptController()
    {
        var html = RepositoryFiles.ReadEmbeddedResource("configPage.html");

        Assert.Contains($"data-controller=\"__plugin/{Plugin.ConfigScriptName}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigPage_ContainsEveryElementTheScriptUses()
    {
        var html = RepositoryFiles.ReadEmbeddedResource("configPage.html");
        var script = RepositoryFiles.ReadEmbeddedResource("configPage.js");
        var ids = Regex.Matches(script, @"querySelector\('#([A-Za-z]+)'\)").Select(m => m.Groups[1].Value).Distinct().ToList();

        Assert.NotEmpty(ids);
        Assert.All(ids, id => Assert.Contains($"id=\"{id}\"", html, StringComparison.Ordinal));
    }

    [Fact]
    public void ConfigScript_UsesTheControllerRoutes()
    {
        var script = RepositoryFiles.ReadEmbeddedResource("configPage.js");

        Assert.Contains("'HyperionGrabber/TestConnection'", script, StringComparison.Ordinal);
        Assert.Contains("'HyperionGrabber/TestPattern'", script, StringComparison.Ordinal);
        Assert.Contains("'HyperionGrabber/Clients'", script, StringComparison.Ordinal);
    }

    private static Plugin CreatePlugin()
    {
        var paths = Substitute.For<IApplicationPaths>();
        paths.PluginsPath.Returns(System.IO.Path.GetTempPath());
        paths.PluginConfigurationsPath.Returns(System.IO.Path.GetTempPath());
        return new Plugin(paths, Substitute.For<IXmlSerializer>());
    }
}
