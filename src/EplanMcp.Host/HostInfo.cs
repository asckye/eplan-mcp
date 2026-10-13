using System.Reflection;

namespace EplanMcp.Host;

/// <summary>Build facts of this Host.</summary>
internal static class HostInfo
{
    /// <summary>Informational version without the source-revision suffix, e.g. <c>0.1.0</c>.</summary>
    public static string Version { get; } =
        (typeof(HostInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];
}
