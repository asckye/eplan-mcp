using System.ComponentModel;
using System.Diagnostics;

namespace EplanMcp.Host.ClientConfig;

/// <summary>
/// Whether Claude Desktop runs: it rewrites claude_desktop_config.json every few tens of seconds (spike S17), so a change
/// from outside could be lost, or lose the app's own. Its processes are named Claude like those of Claude Code's CLI, so
/// the image path tells them apart: the MSIX package under WindowsApps, or the older installer's AnthropicClaude folder.
/// A process whose path cannot be read counts as Claude Desktop.
/// </summary>
internal static class ClaudeDesktopProcess
{
    public static bool IsRunning()
    {
        Process[] processes = Process.GetProcessesByName("claude");
        try
        {
            return processes.Any(p => IsDesktop(ImagePath(p)));
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    internal static bool IsDesktop(string? imagePath) =>
        imagePath is null
        || imagePath.Contains(@"\WindowsApps\Claude_", StringComparison.OrdinalIgnoreCase)
        || imagePath.Contains(@"\AnthropicClaude\", StringComparison.OrdinalIgnoreCase);

    private static string? ImagePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return null;
        }
    }
}
