using System.Globalization;
using EplanMcp.Contracts;
using EplanMcp.Host.Safety;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>
/// The approval dialog's texts (M3 design 4.6): Chinese or English after the user interface language, one line per
/// change, and project data that cannot hide what it is or flood the dialog.
/// </summary>
[Trait("Size", "Small")]
public sealed class ApprovalTextsTests
{
    private static readonly WritePlan _plan = new()
    {
        Project = "Line1",
        ProjectPath = @"D:\Projects\Line1",
        Changes =
        [
            new PlannedChange { Action = "create", Kind = "page", Name = "=A1&EFS1/5" },
            new PlannedChange { Action = "update", Kind = "page", Name = "=A1&EFS1/6", Property = "description", OldValue = null, NewValue = "电源" },
            new PlannedChange { Action = "update", Kind = "device", Name = "-FC1", Property = "tag", OldValue = "-FC1", NewValue = "-QA1" },
        ],
        Fingerprint = "f",
    };

    private static readonly ApprovalQuestion _question = new("r", "eplan_page_create", "h", _plan, [], DateTimeOffset.UnixEpoch);

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("zh-TW")]
    public void InChinese_TheDialogFollowsTheDesign(string culture)
    {
        ApprovalAskArgs texts = ApprovalTexts.For(_question, CultureInfo.GetCultureInfo(culture));

        Assert.Equal("批准 AI 修改 EPLAN 项目？", texts.Title);
        Assert.Equal("工具：eplan_page_create\n项目：Line1（D:\\Projects\\Line1）\n将进行以下修改（共 3 处）：", texts.Summary);
        Assert.Equal(["新建页 =A1&EFS1/5", "修改页 =A1&EFS1/6 的描述：（未设置） → '电源'", "修改设备 -FC1 的标识：'-FC1' → '-QA1'"], texts.Changes);
        Assert.Equal("批准后可在 EPLAN 中按 Ctrl+Z 撤销“AI: eplan_page_create”这一步。", texts.Note);
        Assert.Equal(("批准", "拒绝", "{0} 后自动拒绝"), (texts.ApproveLabel, texts.RejectLabel, texts.CountdownFormat));
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("de-DE")]
    [InlineData("")]
    public void InAnyOtherLanguage_TheDialogIsEnglish(string culture)
    {
        ApprovalAskArgs texts = ApprovalTexts.For(_question, CultureInfo.GetCultureInfo(culture));

        Assert.Equal("Approve AI changes to the EPLAN project?", texts.Title);
        Assert.Equal("Tool: eplan_page_create\nProject: Line1 (D:\\Projects\\Line1)\nThe following changes will be made (3):", texts.Summary);
        Assert.Equal(["Create page =A1&EFS1/5", "Change description of page =A1&EFS1/6: (not set) → '电源'", "Change tag of device -FC1: '-FC1' → '-QA1'"], texts.Changes);
        Assert.Equal("After approval, Ctrl+Z in EPLAN undoes the step \"AI: eplan_page_create\".", texts.Note);
        Assert.Equal(("Approve", "Reject", "Rejected automatically in {0}"), (texts.ApproveLabel, texts.RejectLabel, texts.CountdownFormat));
    }

    [Fact]
    public void ProjectData_StaysOnOneLine_WithoutBidirectionalControls()
    {
        var change = new PlannedChange { Action = "update", Kind = "page", Name = "=A1/1", Property = "description", OldValue = "a\r\nb\tc", NewValue = "x\u202Eevil\u202C\u2066y\u2069" };

        Assert.Equal("Change description of page =A1/1: 'a  b c' → 'xevily'", ApprovalTexts.Change(change, zh: false));
    }

    [Fact]
    public void LongValues_AreShortenedWithTheirLength()
    {
        var change = new PlannedChange { Action = "create", Kind = "page", Name = new string('x', 500) };

        string zh = ApprovalTexts.Change(change, zh: true);
        string en = ApprovalTexts.Change(change, zh: false);

        Assert.Equal("新建页 " + new string('x', ApprovalTexts.MaxValueLength) + "…（共 500 个字符）", zh);
        Assert.Equal("Create page " + new string('x', ApprovalTexts.MaxValueLength) + "… (500 characters)", en);
    }

    [Fact]
    public void TheReleaseConfirmation_InChinese_AsksForACheckNotForCtrlZ()
    {
        var suspension = new Suspension(@"D:\Projects\Line1", null, null, "eplan_page_create", "r0", new DateTimeOffset(2026, 10, 20, 8, 30, 0, TimeSpan.Zero), SuspensionReason.OutcomeUnknown, ["create page =A1&EFS1/5"]);

        ApprovalAskArgs texts = ApprovalTexts.For(_question with { Release = suspension }, CultureInfo.GetCultureInfo("zh-CN"));

        string time = suspension.Time.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        Assert.Equal("上次 AI 修改的结果未知", texts.Title);
        Assert.Equal($"时间：{time}\n工具：eplan_page_create\n项目：D:\\Projects\\Line1\n这次修改被中断，无法确定是否已经执行，项目中可能只有其中一部分。AI 的写入因此暂停。计划中的修改（最多 20 处）：", texts.Summary);
        Assert.Equal(["create page =A1&EFS1/5"], texts.Changes);
        Assert.Equal("请先在 EPLAN 中核实项目：手动删除不完整的修改，或者恢复项目的副本；EPLAN 打开项目时如果提示检查数据，请执行检查。核实之后再继续，否则请保持暂停。", texts.Note);
        Assert.Equal(("已核实，继续", "保持暂停", "{0} 后自动保持暂停"), (texts.ApproveLabel, texts.RejectLabel, texts.CountdownFormat));
        Assert.DoesNotContain("Ctrl+Z", texts.Summary + texts.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReleaseConfirmation_ForAnInstance_InEnglish_WithAnUnreadableRecord()
    {
        var suspension = new Suspension(null, 4242, DateTimeOffset.UnixEpoch, "?", "?", DateTimeOffset.MinValue, SuspensionReason.OutcomeUnknown, []);

        ApprovalAskArgs texts = ApprovalTexts.For(_question with { Release = suspension }, CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal("The outcome of the last AI change is unknown", texts.Title);
        Assert.Equal("Time: ?\nTool: ?\nScope: every project in this EPLAN instance (process 4242)\nThis change was interrupted and whether it was made is unknown; the project may contain part of it. AI writes are suspended. Planned changes (up to 20):", texts.Summary);
        Assert.Equal(["(the suspension record lists no changes)"], texts.Changes);
        Assert.StartsWith("Check the project in EPLAN first: remove any partial change by hand, or restore a copy", texts.Note, StringComparison.Ordinal);
        Assert.Equal(("Checked, continue", "Keep suspended", "Stays suspended automatically in {0}"), (texts.ApproveLabel, texts.RejectLabel, texts.CountdownFormat));
    }

    [Fact]
    public void UnknownKindsAndProperties_AreShownAsTheyAre()
    {
        var change = new PlannedChange { Action = "update", Kind = "cable", Name = "-W1", Property = "length", OldValue = "1", NewValue = "2" };

        Assert.Equal("修改cable -W1 的length：'1' → '2'", ApprovalTexts.Change(change, zh: true));
    }
}
