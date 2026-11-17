using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>What <see cref="IPartsDatabase.Read"/> found.</summary>
public sealed class PartsDatabaseContent
{
    /// <summary>The configured database as EPLAN's setting has it: a file with path variables, or a SQL Server connection string.</summary>
    public string Database { get; set; } = string.Empty;

    /// <summary>Every part, each variant on its own, in no particular order.</summary>
    public List<PartSearchItem> Parts { get; set; } = [];
}
