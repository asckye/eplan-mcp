namespace EplanMcp.Workbench.Protocol;

/// <summary>What a question asks; on the wire <c>approval</c> or <c>suspension_release</c>.</summary>
public enum QuestionKind
{
    /// <summary>May the write make the listed changes?</summary>
    Approval,

    /// <summary>Has the engineer checked the project after the suspended write, so that writes may go on?</summary>
    SuspensionRelease,
}
