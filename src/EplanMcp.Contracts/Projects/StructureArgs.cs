namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.StructureList"/> (docs/tools/eplan_structure_list.md).</summary>
public sealed class StructureListArgs : ListArgs
{
    /// <summary>The prefix of one block (=, ==, ++, +, &amp;, #); null means every block.</summary>
    public string? Block { get; set; }

    /// <summary>Only identifiers nothing in the project uses.</summary>
    public bool UnusedOnly { get; set; }
}

/// <summary>One structure identifier of a project, such as <c>+S1</c>.</summary>
public sealed class StructureIdentifier
{
    /// <summary>The full name with its block's prefix.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The description, serialized as <c>zh_CN@text;en_US@text;</c>; empty when not set.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>Whether a page or object of the project uses it.</summary>
    public bool Used { get; set; }
}

/// <summary>Arguments of <see cref="OperationNames.StructureUpdatePlan"/> and <see cref="OperationNames.StructureUpdateExecute"/>.</summary>
public sealed class StructureUpdateArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>The identifiers to describe, or to create when the project does not have them yet; null when the call only sorts.</summary>
    public List<StructureIdentifierValue>? Identifiers { get; set; }

    /// <summary>Fills in the project's other languages from the translation database for descriptions given in one language.</summary>
    public bool Translate { get; set; }

    /// <summary>Full names in the wanted order, per block; the block's other identifiers follow in their present order. Null keeps the order.</summary>
    public List<string>? Order { get; set; }

    /// <summary>Plan state fingerprint, required for execution.</summary>
    public string? Fingerprint { get; set; }
}

/// <summary>One entry of <see cref="StructureUpdateArgs.Identifiers"/>.</summary>
public sealed class StructureIdentifierValue
{
    /// <summary>The full name with its block's prefix, such as <c>+S1</c>.</summary>
    public string? Name { get; set; }

    /// <summary>The new description, plain or <c>zh_CN@text;en_US@text;</c>; empty clears it, null leaves it as it is.</summary>
    public string? Description { get; set; }
}

/// <summary>Data of <see cref="OperationNames.StructureUpdateExecute"/>.</summary>
public sealed class StructureUpdateData
{
    public string Project { get; set; } = string.Empty;

    /// <summary>The identifiers that were created or described, read back.</summary>
    public List<StructureIdentifier> Identifiers { get; set; } = [];

    public string? Warning { get; set; }
}

/// <summary>Arguments of <see cref="OperationNames.StructureDeletePlan"/> and <see cref="OperationNames.StructureDeleteExecute"/>.</summary>
public sealed class StructureDeleteArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>The unused identifiers to delete; null means every unused identifier.</summary>
    public List<string>? Identifiers { get; set; }

    /// <summary>Plan state fingerprint, required for execution.</summary>
    public string? Fingerprint { get; set; }

    /// <summary>Where the Bridge copies the project first; the Host's pipeline fills it in.</summary>
    public BackupTarget? Backup { get; set; }
}

/// <summary>Data of <see cref="OperationNames.StructureDeleteExecute"/>.</summary>
public sealed class StructureDeleteData
{
    public string Project { get; set; } = string.Empty;

    /// <summary>The identifiers that are gone.</summary>
    public List<StructureIdentifier> Identifiers { get; set; } = [];

    /// <summary>The path of the backup copy.</summary>
    public string? Backup { get; set; }

    public string? Warning { get; set; }
}

/// <summary>Arguments of <see cref="OperationNames.StructureRenamePlan"/> and <see cref="OperationNames.StructureRenameExecute"/>.</summary>
public sealed class StructureRenameArgs
{
    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>The identifiers to rename.</summary>
    public List<StructureRenameValue>? Renames { get; set; }

    /// <summary>Plan state fingerprint, required for execution.</summary>
    public string? Fingerprint { get; set; }

    /// <summary>Where the Bridge copies the project first; the Host's pipeline fills it in.</summary>
    public BackupTarget? Backup { get; set; }
}

/// <summary>One entry of <see cref="StructureRenameArgs.Renames"/>.</summary>
public sealed class StructureRenameValue
{
    /// <summary>The full name now.</summary>
    public string? Name { get; set; }

    /// <summary>The new full name, in the same block and under the same parent.</summary>
    public string? NewName { get; set; }
}

/// <summary>Data of <see cref="OperationNames.StructureRenameExecute"/>.</summary>
public sealed class StructureRenameData
{
    public string Project { get; set; } = string.Empty;

    /// <summary>The identifiers under their new names, read back.</summary>
    public List<StructureIdentifier> Identifiers { get; set; } = [];

    /// <summary>The path of the backup copy.</summary>
    public string? Backup { get; set; }

    public string? Warning { get; set; }
}
