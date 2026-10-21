using System.Diagnostics;
using System.Globalization;

namespace EplanMcp.Spikes.S18;

/// <summary>Command line of both prototypes. The probe (Probe/) passes these; a person starting the exe needs none.</summary>
internal sealed record SpikeOptions(string? LogPath, UiLanguage Language, string Theme, int DeadlineSeconds, bool FontCheck)
{
    internal static SpikeOptions Parse(string[] args)
    {
        string? Value(string name)
        {
            int i = Array.IndexOf(args, name);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        return new SpikeOptions(
            Value("--log"),
            Value("--lang") == "en" ? UiLanguage.English : UiLanguage.Chinese,
            Value("--theme") ?? "system",
            int.Parse(Value("--deadline") ?? "120", CultureInfo.InvariantCulture),
            args.Contains("--font-check"));
    }
}

/// <summary>
/// Tab-separated events with the milliseconds since process creation (kernel start time), so the probe can read the
/// time to the first Loaded and first rendered frame. Written only with --log; never to stdout.
/// </summary>
internal static class SpikeLog
{
    private static readonly DateTime _startTime = Process.GetCurrentProcess().StartTime;
    private static readonly object _gate = new();
    private static string? _path;

    internal static void Init(string? path) => _path = path;

    internal static void Write(string name, string? detail = null)
    {
        if (_path is null)
        {
            return;
        }

        double ms = (DateTime.Now - _startTime).TotalMilliseconds;
        string line = string.Create(CultureInfo.InvariantCulture, $"{ms:F1}\t{name}\t{detail}\n");
        lock (_gate)
        {
            System.IO.File.AppendAllText(_path, line);
        }
    }
}
