namespace EplanMcp.Bridge.Core.Operations;

/// <summary>One labelling scheme of <c>USER.Labelling.Config</c> (api-notes A24, B9) as <see cref="IProjectExporter"/> reads it.</summary>
public sealed class LabelScheme
{
    /// <summary>The scheme's name, as EPLAN's labelling takes it, e.g. <c>Device tag list</c>.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>The converter of the scheme's output (its <c>Data.File.Format</c>, api-notes B9), e.g. <c>XmlLblTextExportConverterImpl</c>; null when it could not be read.</summary>
    public string? Format { get; set; }
}
