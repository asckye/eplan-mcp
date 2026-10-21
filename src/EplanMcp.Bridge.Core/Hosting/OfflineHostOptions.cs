using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Hosting;

/// <summary>
/// The command line of the offline host (M5 design 4.9). Pure parsing, so it is tested without EPLAN; whether the
/// paths exist is checked by the program. Every option takes a value and may be given once.
/// </summary>
public sealed class OfflineHostOptions
{
    public const string Usage =
        "usage: EplanMcp.Bridge.Offline --variant-bin <Bin of an EPLAN variant, e.g. C:\\Program Files\\EPLAN\\Electric P8\\2026.0.3\\Bin> " +
        "--project <.elk of a scratch copy> [--pipe-prefix <prefix, default " + BridgeProtocol.PipePrefix + ">] [--idle-exit <seconds>]";

    /// <summary>The longest pipe prefix accepted; the pipe name adds the process ID and 17 characters.</summary>
    public const int MaxPipePrefixLength = 64;

    /// <summary>The longest idle exit accepted: a day.</summary>
    public const int MaxIdleExitSeconds = 86_400;

    /// <summary>The variant's Bin (with W3u.exe), not the Platform's: <c>EplApplication.EplanBinFolder</c> (api-notes A22).</summary>
    public string VariantBin { get; private set; } = string.Empty;

    /// <summary>The <c>.elk</c> file of the project opened at start; the Host's policy decides what may be written to it.</summary>
    public string Project { get; private set; } = string.Empty;

    /// <summary>The pipe name's prefix; a Host finds another one than the add-in's only through <c>EPLANMCP_PIPE_PREFIX</c>.</summary>
    public string PipePrefix { get; private set; } = BridgeProtocol.PipePrefix;

    /// <summary>Exit once no Host has been connected for this long; null: run until stopped.</summary>
    public TimeSpan? IdleExit { get; private set; }

    /// <summary>The options, or null with what is wrong in <paramref name="problem"/>.</summary>
    public static OfflineHostOptions? Parse(IReadOnlyList<string> args, out string? problem)
    {
        var options = new OfflineHostOptions();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Count; i += 2)
        {
            string name = args[i];
            string? value = i + 1 < args.Count ? args[i + 1] : null;
            problem = !seen.Add(name) ? $"{name} is given twice."
                : value is null || value.Length == 0 || value.StartsWith("--", StringComparison.Ordinal) ? $"{name} needs a value."
                : options.Set(name, value);
            if (problem is not null)
            {
                return null;
            }
        }

        problem = options.VariantBin.Length == 0 ? "--variant-bin is required."
            : options.Project.Length == 0 ? "--project is required."
            : null;
        return problem is null ? options : null;
    }

    private string? Set(string name, string value)
    {
        switch (name)
        {
            case "--variant-bin":
                VariantBin = value;
                return IsFullPath(value) ? null : "--variant-bin must be a full path.";
            case "--project":
                Project = value;
                return IsFullPath(value) && value.EndsWith(".elk", StringComparison.OrdinalIgnoreCase) ? null : "--project must be the full path of a project's .elk file.";
            case "--pipe-prefix":
                PipePrefix = value;
                return value.Length <= MaxPipePrefixLength && value.All(c => c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '.' or '-' or '_')
                    ? null
                    : $"--pipe-prefix takes up to {MaxPipePrefixLength.ToString(CultureInfo.InvariantCulture)} letters, digits, '.', '-' or '_'.";
            case "--idle-exit":
                bool valid = int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int seconds) && seconds is >= 1 and <= MaxIdleExitSeconds;
                IdleExit = TimeSpan.FromSeconds(seconds);
                return valid ? null : $"--idle-exit takes whole seconds from 1 to {MaxIdleExitSeconds.ToString(CultureInfo.InvariantCulture)}.";
            default:
                return $"Unknown option '{name}'.";
        }
    }

    /// <summary>.NET Framework's IsPathRooted throws on characters such as '|'; those are no path either.</summary>
    private static bool IsFullPath(string value) => value.IndexOfAny(Path.GetInvalidPathChars()) < 0 && Path.IsPathRooted(value);
}
