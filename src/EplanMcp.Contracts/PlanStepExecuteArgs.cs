namespace EplanMcp.Contracts;

/// <summary>One guarded write inside invoke. Contains approved facts, never an approval answer.</summary>
public sealed class PlanStepExecuteArgs
{
    /// <summary>Lowercase 32-hex plan identifier.</summary>
    public string PlanId { get; set; } = string.Empty;

    /// <summary>Original one-based step index.</summary>
    public int Index { get; set; }

    /// <summary>Total original steps, from 1 to 20.</summary>
    public int Total { get; set; }

    /// <summary>Complete P16 guard input, freshly checked resources and locked binding included.</summary>
    public PlanStepGuardArgs Guard { get; set; } = new();

    /// <summary>Approved ordered changes, including predicted changes for a dependent step.</summary>
    public List<PlannedChange> ApprovedChanges { get; set; } = [];

    /// <summary>Approved stable plan details; null for predicted macro insertion and operations without details.</summary>
    public Dictionary<string, string>? ApprovedDetails { get; set; }

    /// <summary>Temporary fingerprint from this apply's most recent same-named plan.</summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>Fresh per-step backup target for a Destructive operation, additional to the initial plan backup.</summary>
    public BackupTarget? Backup { get; set; }

    /// <summary>Compiled complete macro description and file digest; required for macro insertion.</summary>
    public PlanMacroConstraint? MacroConstraint { get; set; }

    /// <summary>Exactly one typed argument must be supplied; nested fingerprint and backup must be null.</summary>
    public PageCreateArgs? PageCreate { get; set; }

    /// <summary>Page description update arguments.</summary>
    public PageUpdateArgs? PageUpdate { get; set; }

    /// <summary>Explicit page deletion arguments.</summary>
    public PageDeleteArgs? PageDelete { get; set; }

    /// <summary>Explicit page renumbering arguments.</summary>
    public PageRenumberArgs? PageRenumber { get; set; }

    /// <summary>Device tag update arguments.</summary>
    public DeviceUpdateArgs? DeviceUpdate { get; set; }

    /// <summary>Device renumbering arguments; explicit pages are required.</summary>
    public DeviceRenumberArgs? DeviceRenumber { get; set; }

    /// <summary>Empty Circuit page, MultiLine variant 0, numbering keep insertion arguments.</summary>
    public MacroInsertArgs? MacroInsert { get; set; }
}
