using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Safety;

/// <summary>Why writes were suspended (ADR-0010 section 3).</summary>
internal enum SuspensionReason
{
    /// <summary>The outcome of a write could not be learned.</summary>
    OutcomeUnknown,

    /// <summary>The write was still running at its limit; a later definite outcome releases it.</summary>
    StillRunning,
}

/// <summary>
/// A suspension of writes to <see cref="Project"/> (a canonical path), to the master-data database
/// <see cref="Database"/> (as EPLAN's setting names it, ADR-0019), or to every project of the EPLAN instance
/// <see cref="ProcessId"/> started at <see cref="ProcessStart"/>. <see cref="Changes"/>: the plan's first lines, for the
/// engineer's check.
/// </summary>
internal sealed record Suspension(
    string? Project,
    int? ProcessId,
    DateTimeOffset? ProcessStart,
    string Tool,
    string RequestId,
    DateTimeOffset Time,
    SuspensionReason Reason,
    IReadOnlyList<string> Changes,
    string? Database = null);

/// <summary>
/// The suspensions in <c>state\suspended\</c> (ADR-0010 section 3, M3 design 4.3), one JSON file each, shared by every
/// Host process: a project's survives restarts; an instance's lasts while that EPLAN process lives, recognised by its
/// ID and start time. Files are written to a temporary name and moved into place. No tool can remove one.
/// </summary>
internal sealed class SuspensionStore(string stateDirectory)
{
    public const int MaxChanges = 20;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private string SuspendedDirectory => Path.Combine(stateDirectory, "suspended");

    public static SuspensionStore For(HostDataLocations locations) => new(locations.State);

    /// <summary>
    /// Records one suspension for the project, one for the master-data database the write changed too, if any, keyed
    /// by its path (ADR-0019), and one for the instance (ADR-0010: all). An empty <paramref name="project"/> is none.
    /// </summary>
    public void Suspend(string project, int processId, DateTimeOffset processStart, string tool, string requestId, DateTimeOffset time, SuspensionReason reason, IEnumerable<string> changes, MasterDataTarget? masterData = null)
    {
        string[] lines = [.. changes.Take(MaxChanges)];
        if (project.Length > 0)
        {
            Write(ProjectFile(project), new Suspension(Canonical(project), null, null, tool, requestId, time, reason, lines));
        }

        if (masterData is not null)
        {
            Write(DatabaseFile(masterData), new Suspension(null, null, null, tool, requestId, time, reason, lines, masterData.Database));
        }

        Write(InstanceFile(processId, processStart), new Suspension(null, processId, processStart, tool, requestId, time, reason, lines));
    }

    /// <summary>The suspensions that stop a write to <paramref name="project"/> and <paramref name="masterData"/> in that EPLAN instance.</summary>
    public IReadOnlyList<Suspension> Blocking(string project, int processId, DateTimeOffset processStart, MasterDataTarget? masterData = null) =>
        [.. Files(project, processId, processStart, masterData).Select(Read).OfType<Suspension>()];

    /// <summary>Every suspension still in force; an instance's whose EPLAN has exited is deleted on the way.</summary>
    public IReadOnlyList<Suspension> All()
    {
        if (!Directory.Exists(SuspendedDirectory))
        {
            return [];
        }

        var all = new List<Suspension>();
        foreach (string file in Directory.EnumerateFiles(SuspendedDirectory, "*.json").Order(StringComparer.Ordinal))
        {
            if (Read(file) is not { } suspension)
            {
                continue;
            }

            if (suspension.ProcessId is { } processId && !IsRunning(processId, suspension.ProcessStart))
            {
                TryDelete(file);
                continue;
            }

            all.Add(suspension);
        }

        return all;
    }

    /// <summary>Lifts the suspensions that blocked a write; only after the engineer confirmed (ADR-0010 section 3).</summary>
    public void Release(string project, int processId, DateTimeOffset processStart, MasterDataTarget? masterData = null)
    {
        foreach (string file in Files(project, processId, processStart, masterData))
        {
            TryDelete(file);
        }
    }

    /// <summary>The start time that identifies an EPLAN process together with its ID; null when it has exited.</summary>
    public static DateTimeOffset? StartOf(int processId)
    {
        try
        {
            using Process process = Process.GetProcessById(processId);
            return new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }

    private static bool IsRunning(int processId, DateTimeOffset? start) => StartOf(processId) is { } now && now == start;

    private string ProjectFile(string project)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(project).ToUpperInvariant()));
        return Path.Combine(SuspendedDirectory, "project-" + Convert.ToHexStringLower(hash)[..16] + ".json");
    }

    /// <summary>
    /// A database's suspension, keyed by its kind and its full path when EPLAN gave one (canonical, as a project's), else
    /// by its setting, so the same database is suspended however a later plan spells it.
    /// </summary>
    private string DatabaseFile(MasterDataTarget masterData)
    {
        string key = masterData.Path.Length > 0 && Path.IsPathFullyQualified(masterData.Path) ? Canonical(masterData.Path) : masterData.Database;
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(masterData.Kind + "\n" + key.ToUpperInvariant()));
        return Path.Combine(SuspendedDirectory, "database-" + Convert.ToHexStringLower(hash)[..16] + ".json");
    }

    private IEnumerable<string> Files(string project, int processId, DateTimeOffset processStart, MasterDataTarget? masterData)
    {
        if (project.Length > 0)
        {
            yield return ProjectFile(project);
        }

        if (masterData is not null)
        {
            yield return DatabaseFile(masterData);
        }

        yield return InstanceFile(processId, processStart);
    }

    private string InstanceFile(int processId, DateTimeOffset start) =>
        Path.Combine(SuspendedDirectory, string.Create(CultureInfo.InvariantCulture, $"instance-{processId}-{start.UtcTicks}.json"));

    /// <summary>The same canonical form as the scratch check, so path tricks cannot dodge a suspension either.</summary>
    private static string Canonical(string project)
    {
        try
        {
            return ScratchPaths.Canonical(project);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return Path.GetFullPath(project);
        }
    }

    private void Write(string file, Suspension suspension)
    {
        Directory.CreateDirectory(SuspendedDirectory);
        string temporary = file + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(suspension, _json));
        File.Move(temporary, file, overwrite: true);
    }

    private static Suspension? Read(string file)
    {
        try
        {
            return File.Exists(file) ? JsonSerializer.Deserialize<Suspension>(File.ReadAllText(file), _json) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // A record that cannot be read still means "suspended" (fail closed); it just cannot be shown in detail.
            return new Suspension(null, null, null, "?", "?", DateTimeOffset.MinValue, SuspensionReason.OutcomeUnknown, []);
        }
    }

    private static void TryDelete(string file)
    {
        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // still in force; the next check tries again
        }
    }
}
