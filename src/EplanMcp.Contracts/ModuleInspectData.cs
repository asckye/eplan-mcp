namespace EplanMcp.Contracts;

/// <summary>Detached evidence validated by ModuleInspectionReader; nested rows follow the module.inspect evidence contract.</summary>
public sealed class ModuleInspectData
{
    /// <summary>The inspected macro's exact digest.</summary>
    public string MacroHash { get; set; } = string.Empty;
    /// <summary>The API Extension licence confirmed during this read; null means unknown.</summary>
    public bool? ApiExtensionLicence { get; set; }
    /// <summary>True only with all six coverage proofs and no discovery gaps.</summary>
    public bool Complete { get; set; }
    /// <summary>functions/pages/connections/crossPageReferences/placeholderBindings/properties proofs.</summary>
    public Dictionary<string, bool> Coverage { get; set; } = new(StringComparer.Ordinal);
    /// <summary>Rows {id,kind,name,page,properties}; property rows {propertyId,index,language,state,value?}.</summary>
    public List<object> Objects { get; set; } = [];
    /// <summary>Rows {from,to,kind}; every endpoint is retained, including cross-page targets.</summary>
    public List<object> References { get; set; } = [];
    /// <summary>Rows {object,name,variables,valueSets,bindings}; languages and variable keys are preserved verbatim.</summary>
    public List<object> Placeholders { get; set; } = [];
    /// <summary>Rows {pointer,message}; native discovery failures use safe English hints, never native exception text.</summary>
    public List<object> Gaps { get; set; } = [];
}
