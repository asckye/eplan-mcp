using System.Diagnostics;

namespace EplanMcp.Host.Addin;

/// <summary>
/// Where install-addin looks on this machine: Program Files (EPLAN), the Public folder (EPLAN's station settings), this
/// user's profile (Q15), the folder of the running eplanmcp.exe (the add-in next to it), and how to read a file version.
/// Tests build one over a temporary directory, so they never touch EPLAN or its settings.
/// </summary>
internal sealed record AddinMachine(string ProgramFiles, string Public, string UserProfile, string HostDirectory, Func<string, string?> FileVersion)
{
    public static AddinMachine FromWindows() => new(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments))!,
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        AppContext.BaseDirectory,
        exe => FileVersionInfo.GetVersionInfo(exe).FileVersion);
}
