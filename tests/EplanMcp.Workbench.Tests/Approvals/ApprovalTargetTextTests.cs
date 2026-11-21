using EplanMcp.Host.Tests.Safety;
using EplanMcp.Workbench.Approvals;
using EplanMcp.Workbench.Protocol;
using TiaOpenness.Gui.Localization;

namespace EplanMcp.Workbench.Tests.Approvals;

/// <summary>
/// ADR-0064 decision 3 on the Workbench side: a destructive question confirms with the target the Host named, the count
/// for a batch, else the project name; the button words come from the Workbench's own vocabulary, never from the Host.
/// </summary>
[Collection(WpfTestGroup.Name)]
[Trait("Size", "Small")]
public sealed class ApprovalTargetTextTests(WpfContext wpf)
{
    private readonly ManualTime _clock = new(Asks.Now);

    [Fact]
    public void OneNamedTarget_IsTyped_AndTheButtonSaysWhatItDoes() => wpf.RunWithLanguage(AppLanguage.Chinese, () =>
    {
        QuestionItem item = Item(new ApprovalTarget("page", "=A1&EFS1/5", 1), "delete");

        Assert.Equal("=A1&EFS1/5", item.ConfirmValue);
        Assert.Equal("输入页标识以确认：=A1&EFS1/5", item.ConfirmLabel);
        Assert.Equal("删除页…", item.ApproveLabel);
    });

    [Fact]
    public void ABatch_IsConfirmedByItsCount() => wpf.RunWithLanguage(AppLanguage.Chinese, () =>
    {
        QuestionItem item = Item(new ApprovalTarget("device", null, 12), "renumber");

        Assert.Equal("12", item.ConfirmValue);
        Assert.Equal("将影响 12 个设备。输入个数以确认：12", item.ConfirmLabel);
        Assert.Equal("重编号设备…", item.ApproveLabel);
    });

    [Theory]
    [InlineData("=A1&EFS1/5\u200B")]
    [InlineData(" =A1&EFS1/5")]
    [InlineData("=A1&EFS1/5\n")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789")]
    public void AnIdTooLongOrWithHiddenCharacters_FallsBackToTheProjectName(string id) => wpf.RunWithLanguage(AppLanguage.Chinese, () =>
    {
        Assert.Equal("Line1", Item(new ApprovalTarget("page", id, 1), "delete").ConfirmValue);
    });

    [Fact]
    public void NoTarget_OrWordsThisWorkbenchDoesNotKnow_FallBackToTheProjectAndTheTool() => wpf.RunWithLanguage(AppLanguage.Chinese, () =>
    {
        QuestionItem none = Item(null, null);
        Assert.Equal(("Line1", "执行 eplan_page_delete…"), (none.ConfirmValue, none.ApproveLabel));

        QuestionItem unknownAction = Item(new ApprovalTarget("page", "=A1&EFS1/5", 1), "explode");
        Assert.Equal("执行 eplan_page_delete…", unknownAction.ApproveLabel);

        QuestionItem unknownKind = Item(new ApprovalTarget("rack", "R1", 1), "delete");
        Assert.Equal("删除对象…", unknownKind.ApproveLabel);
    });

    private QuestionItem Item(ApprovalTarget? target, string? action) =>
        new(new ApprovalQueue(_clock).Add(Asks.Write("r1", tier: QuestionTier.Destructive, tool: "eplan_page_delete") with { Target = target, Action = action })!, _clock);
}
