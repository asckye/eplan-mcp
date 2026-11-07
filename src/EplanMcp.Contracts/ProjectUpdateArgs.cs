namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.ProjectUpdatePlan"/> and <see cref="OperationNames.ProjectUpdateExecute"/> (ADR-0052).</summary>
public sealed class ProjectUpdateArgs
{
    /// <summary>The title block properties a call may set, and whether each holds a multi-language text. No tool or configuration can add entries.</summary>
    public static readonly IReadOnlyDictionary<int, bool> TitleBlock = new Dictionary<int, bool>
    {
        [10011] = true, // PROJ_INSTALLATIONNAME, project description
        [10013] = false, // PROJ_DRAWINGNUMBER
        [10014] = true, // PROJ_COMMISSION
        [10015] = false, // PROJ_COMPANYNAME
        [10016] = false, // PROJ_COMPANYADDRESS1
        [10017] = false, // PROJ_COMPANYADDRESS2
        [10020] = false, // PROJ_CREATOR
        [10025] = false, // PROJ_RESPONSIBLEFORPROJECT
        [10030] = true, // PROJ_MAKE
        [10031] = true, // PROJ_TYPE
        [10032] = true, // PROJ_LOCATIONOFINSTALLATION
        [10100] = false, // PROJ_CUSTOMERID
        [10232] = false, // PROJ_CREATORNAME1
        [10235] = false, // PROJ_CREATORSTREET
        [10237] = false, // PROJ_CREATORZIPCODE
        [10238] = false, // PROJ_CREATORCITY
        [10240] = false, // PROJ_CREATORTELEPHONE
    };

    /// <summary>The prefix of a template's own project properties, set by identifying name; they only appear where a form shows them.</summary>
    public const string UserDefinedPrefix = "ESS.Project.";

    /// <summary>Project name or path as eplan_project_list returns it; required for writes.</summary>
    public string? Project { get; set; }

    /// <summary>The properties to set, each once.</summary>
    public List<ProjectPropertyValue>? Properties { get; set; }

    /// <summary>Fills in the project's other languages from the translation database for multi-language values given in one language.</summary>
    public bool Translate { get; set; }

    /// <summary>Plan state fingerprint, required for execution.</summary>
    public string? Fingerprint { get; set; }
}

/// <summary>One property of <see cref="ProjectUpdateArgs"/>.</summary>
public sealed class ProjectPropertyValue
{
    /// <summary>The property ID, one of <see cref="ProjectUpdateArgs.TitleBlock"/>; 0 when <see cref="Name"/> is given.</summary>
    public int Id { get; set; }

    /// <summary>A user-defined project property by identifying name, such as <c>ESS.Project.Control_Voltage</c>.</summary>
    public string? Name { get; set; }

    /// <summary>The new text, at most 500 characters; empty clears it. A multi-language property also takes <c>zh_CN@text;en_US@text;</c>.</summary>
    public string? Value { get; set; }
}

/// <summary>Data of <see cref="OperationNames.ProjectUpdateExecute"/>.</summary>
public sealed class ProjectUpdateData
{
    public string Project { get; set; } = string.Empty;

    /// <summary>The properties that changed, read back.</summary>
    public List<ProjectPropertyChange> Properties { get; set; } = [];

    public string? Warning { get; set; }
}

/// <summary>One changed property of <see cref="ProjectUpdateData"/>.</summary>
public sealed class ProjectPropertyChange
{
    public string Property { get; set; } = string.Empty;
    public string Previous { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
