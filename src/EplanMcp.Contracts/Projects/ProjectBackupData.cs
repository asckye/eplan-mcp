namespace EplanMcp.Contracts;

/// <summary>A verified Snapshot copy of an open project, without a project transaction (A18/A41).</summary>
public sealed class ProjectBackupData
{
    /// <summary>The full path of the verified copy's .elk file.</summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>The copy's page count, checked against the source; zero is valid.</summary>
    public int Pages { get; set; }

    /// <summary>The time spent copying and checking the pages, in seconds.</summary>
    public decimal Seconds { get; set; }
}
