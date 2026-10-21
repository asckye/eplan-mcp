using EplanMcp.Workbench.Protocol;

namespace EplanMcp.Workbench.Tests.Approvals;

/// <summary>Questions as a Host sends them, for the tests.</summary>
internal static class Asks
{
    public static readonly DateTimeOffset Now = new(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);

    public static ApprovalAskParams Write(string requestId = "r1", TimeSpan? left = null, QuestionTier tier = QuestionTier.Write, string tool = "eplan_page_create") => new(
        requestId,
        QuestionKind.Approval,
        tool,
        tier,
        new ProjectInfo("Line1", @"D:\Projects\Line1"),
        new EplanInstance("2026.0.3.25421", "Pro Panel", 4242),
        "claude-code",
        ["Create page =A1&EFS1/5"],
        tier == QuestionTier.Destructive ? @"D:\EplanMcp\backups\Line1" : null,
        null,
        "f1",
        "h1",
        (Now + (left ?? TimeSpan.FromSeconds(120))).ToUnixTimeMilliseconds());

    public static ApprovalAskParams Release(string requestId = "r1", TimeSpan? left = null) => Write(requestId, left) with
    {
        Kind = QuestionKind.SuspensionRelease,
        Suspension = new SuspensionRecord("earlier", "eplan_page_delete", Now.AddHours(-1).ToUnixTimeMilliseconds(), @"D:\Projects\Line1", null),
    };
}
