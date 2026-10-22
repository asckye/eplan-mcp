using System.Globalization;
using System.Text.RegularExpressions;

namespace EplanMcp.Spikes.S26;

// Shared by the add-in and driver. No EPLAN types or calls belong here.
internal static class SpikeS26Paths
{
    internal static readonly string Scratch = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp", "scratch");
    internal static readonly string OutputRoot = Path.Combine(Scratch, "spikes", "s26");
    internal static readonly Dictionary<string, int> Timeouts = new(StringComparer.Ordinal)
    {
        ["runner"] = 300, ["layers"] = 180, ["parts"] = 1200,
        ["plc"] = 600, ["properties"] = 300, ["external"] = 600,
    };

    internal static string Below(string path, string root)
    {
        string full = Path.GetFullPath(path).TrimEnd('\\', '/');
        string boundary = Path.GetFullPath(root).TrimEnd('\\') + "\\";
        if (!Path.IsPathRooted(path) || full.StartsWith(@"\\", StringComparison.Ordinal) || !full.StartsWith(boundary, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("The path must be strictly below the allowed local root.");
        }

        // Reject links, including the root itself. Do not resolve a link by following it.
        for (string? item = full; item is not null && item.Length >= boundary.Length - 1; item = Path.GetDirectoryName(item))
        {
            if ((File.Exists(item) || Directory.Exists(item)) && (File.GetAttributes(item) & FileAttributes.ReparsePoint) != 0)
            {
                throw new ArgumentException("Reparse points are not allowed in spike paths.");
            }
        }

        return full;
    }

    internal static string Project(string path)
    {
        string full = Below(path, Scratch);
        if (!File.Exists(full) || !string.Equals(Path.GetExtension(full), ".elk", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Use an existing scratch .elk copy, already open in EPLAN.");
        }

        Below(Path.ChangeExtension(full, ".edb"), Scratch);
        return full;
    }

    internal static string RunFolder(string path)
    {
        string full = Below(path, OutputRoot);
        Below(full, Scratch); // Also check the spikes/s26 ancestors, not just the run folder's immediate root.
        if (!string.Equals(Path.GetDirectoryName(full), OutputRoot, StringComparison.OrdinalIgnoreCase) ||
            !Regex.IsMatch(Path.GetFileName(full), @"\A\d{8}T\d{13}Z-[a-f0-9]{32}\z", RegexOptions.CultureInvariant))
        {
            throw new ArgumentException("Use s26/<UTC>-<id> as the run folder.");
        }

        return full;
    }

    internal static string NewRunFolder() => RunFolder(Path.Combine(OutputRoot,
        DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffffffZ", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")));

    internal static string Report(string folder, string mode) => Path.Combine(RunFolder(folder), "s26-" + Mode(mode) + ".json");

    internal static string Mode(string mode) => Timeouts.ContainsKey(mode) ? mode : throw new ArgumentException("Unknown S26 mode.");

    internal static void Reserve(string folder, string mode, string suffix)
    {
        // CreateNew is the cross-process no-repeat gate; markers survive timeout/crash.
        string marker = Path.Combine(RunFolder(folder), Mode(mode) + suffix);
        using var stream = new FileStream(marker, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
    }
}
