namespace EplanMcp.Workbench.Protocol;

/// <summary>The tier of the tool a question is about (ADR-0005); on the wire <c>write</c> or <c>destructive</c>.</summary>
public enum QuestionTier
{
    /// <summary>Changes the project; Ctrl+Z in EPLAN undoes it.</summary>
    Write,

    /// <summary>Deletes or renumbers; the project is backed up first (ADR-0013). Shown prominently.</summary>
    Destructive,
}
