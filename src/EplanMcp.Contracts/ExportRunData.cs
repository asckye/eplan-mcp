namespace EplanMcp.Contracts;

/// <summary>Result data of the export runs (<c>export.*.run</c>). The Host lists the folder's files itself.</summary>
public sealed class ExportRunData
{
    /// <summary>The name of the project; empty for the database exports.</summary>
    public string Project { get; set; } = string.Empty;

    /// <summary>How many pages were exported; 0 for the exports of a whole project (PXF, lists).</summary>
    public int PageCount { get; set; }

    /// <summary>PDF, DXF, labels: the scheme used; null otherwise.</summary>
    public string? Scheme { get; set; }

    /// <summary>Images: the file each page became, in the order of the pages; null for PDF and DXF.</summary>
    public List<ExportedPage>? Files { get; set; }

    /// <summary>What the engineer should know although the export worked, for example a scheme setting not set back.</summary>
    public string? Warning { get; set; }

    /// <summary>Untranslated texts: the languages exported, as the project names them; null otherwise.</summary>
    public List<string>? Languages { get; set; }

    /// <summary>The database exports: the database exported, as <see cref="ExportPlanData.Database"/> names it; null otherwise.</summary>
    public string? Database { get; set; }

    /// <summary>Parts database: how many parts the file has, a part with several variants counted once; null otherwise.</summary>
    public int? PartCount { get; set; }

    /// <summary>Parts database: how many parts were exported with each variant counted; null otherwise.</summary>
    public int? VariantCount { get; set; }
}
