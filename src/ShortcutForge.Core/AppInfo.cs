using System.Reflection;

namespace ShortcutForge.Core;

/// <summary>Product name and version (set once in Directory.Build.props).</summary>
public static class AppInfo
{
    public const string Name = "ShortcutForge";

    public const string RepositoryUrl = "https://github.com/kipergrof/ShortcutForge";

    /// <summary>Semantic version, e.g. "1.0.0".</summary>
    public static string Version { get; } =
        (typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0")
        .Split('+')[0]; // drop the source revision suffix

    /// <summary>HTTP User-Agent used when talking to online services.</summary>
    public static string UserAgent => $"{Name}/{Version} (+{RepositoryUrl})";
}
