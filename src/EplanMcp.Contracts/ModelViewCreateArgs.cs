namespace EplanMcp.Contracts;

/// <summary>Arguments of <see cref="OperationNames.ModelViewCreatePlan"/> and <see cref="OperationNames.ModelViewCreateExecute"/>.</summary>
public sealed class ModelViewCreateArgs
{
    /// <summary>Project name or path; required for a write.</summary>
    public string? Project { get; set; }

    /// <summary>Installation space designation, e.g. <c>UC1</c>, with an enclosure in it.</summary>
    public string? Space { get; set; }

    /// <summary>Full name of an existing ModelView or PanelLayout page.</summary>
    public string? Page { get; set; }

    /// <summary><c>panel</c> (default), <c>door</c> or <c>cabinet</c> (the whole installation space).</summary>
    public string? View { get; set; }

    /// <summary><c>front</c> (default), <c>rear</c>, <c>left</c>, <c>right</c>, <c>top</c>, <c>bottom</c> or one of the isometric views.</summary>
    public string? Viewpoint { get; set; }

    /// <summary><c>hidden_lines</c> (default), <c>wireframe</c> or <c>shaded</c>.</summary>
    public string? Style { get; set; }

    /// <summary><c>auto</c> (default) or <c>fit</c>.</summary>
    public string? Scale { get; set; }

    /// <summary>Lower left corner of the view on the page, in page millimetres.</summary>
    public double? X { get; set; }

    /// <summary>Lower left corner of the view on the page, in page millimetres.</summary>
    public double? Y { get; set; }

    /// <summary>Width of the view on the page, in millimetres.</summary>
    public double? Width { get; set; }

    /// <summary>Height of the view on the page, in millimetres.</summary>
    public double? Height { get; set; }

    /// <summary>The view's name; default from the view, e.g. <c>UC1 panel</c>.</summary>
    public string? Name { get; set; }

    /// <summary>For the execution: the fingerprint the plan returned.</summary>
    public string? Fingerprint { get; set; }
}
