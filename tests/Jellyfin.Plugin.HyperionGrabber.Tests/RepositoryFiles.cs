using System;
using System.IO;
using System.Reflection;

namespace Jellyfin.Plugin.HyperionGrabber.Tests;

internal static class RepositoryFiles
{
    private static readonly Lazy<string> Root = new(FindRoot);

    public static Assembly PluginAssembly => typeof(Plugin).Assembly;

    public static string ReadAllText(string relativePath) => File.ReadAllText(Path.Combine(Root.Value, relativePath));

    public static string ReadEmbeddedResource(string fileName)
    {
        var assembly = typeof(Plugin).Assembly;
        var name = typeof(Plugin).Namespace + ".Configuration." + fileName;
        using var stream = assembly.GetManifestResourceStream(name) ?? throw new FileNotFoundException($"Embedded resource {name} not found.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Jellyfin.Plugin.HyperionGrabber.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
