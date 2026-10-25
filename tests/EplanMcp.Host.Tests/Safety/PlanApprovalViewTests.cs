using System.Globalization;
using System.Text;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tests.Plans;
using EplanMcp.Workbench.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace EplanMcp.Host.Tests.Safety;

[Trait("Size", "Small")]
public sealed class PlanApprovalViewTests
{
    private static readonly CultureInfo _en = CultureInfo.GetCultureInfo("en-US");
    private static readonly CultureInfo _zh = CultureInfo.GetCultureInfo("zh-CN");
    private static EplanInstanceInfo Eplan => new() { Version = "2026", Variant = "Pro Panel", ProcessId = 42 };

    [Fact]
    public void Workbench_MetadataLimitsAndWholePlainPath_PrecedeGroupedChanges()
    {
        PlanApprovalView view = PlanApprovalTestData.View();
        List<string> lines = view.WorkbenchChanges(_en);
        int path = lines.IndexOf(PlanApprovalTestData.ChangesPath);
        int firstStep = lines.FindIndex(l => l.StartsWith("Step ", StringComparison.Ordinal));

        Assert.StartsWith("Plan ID: " + view.Record.Definition.PlanId, lines[0], StringComparison.Ordinal);
        Assert.Equal("planHash: " + view.Record.PlanHash, lines[1]);
        Assert.Contains("Cabinet: default", lines);
        Assert.Contains("Highest tier: Write", lines);
        Assert.Contains("Backup location: " + PlanApprovalTestData.Backup, lines);
        Assert.True(path > 0 && path < firstStep);
        Assert.Contains(lines.Take(path), l => l.Contains("current project", StringComparison.Ordinal));
        Assert.Contains(lines.Take(path), l => l.Contains("eplan_plan_get", StringComparison.Ordinal));
        Assert.Equal("SHA-256: " + view.Record.Definition.Digests.ChangesDigest, lines[path + 1]);
        Assert.Contains("  Page +A/1", lines);
        Assert.Contains(lines, l => l.Contains("'Before' -> 'Example'", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, l => l.Contains("Summary —", StringComparison.Ordinal));
    }

    [Fact]
    public void Summary_ReservesAllStepsAndMarksDestructiveAndPredictedPatterns()
    {
        PlanRecord record = PlanApprovalTestData.Record(240);
        PlanStep first = record.Definition.Steps[0];
        PlanStep last = first with
        {
            Index = 20,
            Op = "page.delete",
            Tier = ToolTier.Destructive,
            Prediction = PlanTestData.Json("{}"),
            Expected = [PlanTestData.Json("""{"tag":"macro","value":{"namePattern":"\\A\\+A-Q[0-9]+\\z"}}""")],
        };
        var view = PlanApprovalTestData.View(PlanApprovalTestData.WithDigest(record.Definition with { Steps = [first, last] }));
        List<string> lines = view.WorkbenchChanges(_en);

        Assert.Equal(200, lines.Count);
        Assert.Contains("Summary — full list in the file.", lines);
        Assert.Contains("Highest tier: Destructive", lines);
        Assert.Contains(lines, l => l.StartsWith("Step 20: page.delete [Destructive]", StringComparison.Ordinal));
        Assert.Contains(lines, l => l.Contains("namePattern", StringComparison.Ordinal));
        Assert.Contains("Predicted steps match approved patterns; see the list for the complete patterns.", lines);
        Assert.All(lines, l => Assert.True(l.Length <= 400));
    }

    [Fact]
    public void FullList_WrapsLongValuesWithoutDroppingUnicodeScalars()
    {
        PlanApprovalView view = PlanApprovalTestData.View(PlanApprovalTestData.Record(value: new string('x', 398) + "😀" + new string('y', 410)));
        List<string> lines = view.WorkbenchChanges(_en);
        string joined = string.Join("", lines.SkipWhile(l => !l.StartsWith("Step ", StringComparison.Ordinal)));
        string full = PlanDiffPreview.For(view.Record.Definition.Steps).Replace("\n", "", StringComparison.Ordinal);

        Assert.Equal(full, joined);
        Assert.All(lines, l => Assert.True(l.Length <= 400));
        Assert.DoesNotContain(lines, l => l.Length > 0 && (char.IsHighSurrogate(l[^1]) || char.IsLowSurrogate(l[0])));
    }

    [Fact]
    public void ExactLineBudget_ShowsEverything_OneMoreChangeRequiresASummary()
    {
        int fixedLines = PlanApprovalTestData.View().WorkbenchChanges(_en).Count - 1;
        PlanApprovalView exact = PlanApprovalTestData.View(PlanApprovalTestData.Record(200 - fixedLines));
        PlanApprovalView over = PlanApprovalTestData.View(PlanApprovalTestData.Record(201 - fixedLines));

        Assert.Equal(200, exact.WorkbenchChanges(_en).Count);
        Assert.DoesNotContain("Summary — full list in the file.", exact.WorkbenchChanges(_en));
        Assert.Equal(200, over.WorkbenchChanges(_en).Count);
        Assert.Contains("Summary — full list in the file.", over.WorkbenchChanges(_en));
    }

    [Fact]
    public void RequiredMetadata_WhichCannotFit_IsRefusedWithoutLosingThePath()
    {
        PlanRecord record = PlanApprovalTestData.Record();
        record = record with
        {
            Definition = record.Definition with { Project = new("large " + new string('p', 400 * 200), "PROJECT", null) },
        };

        BridgeCallException error = Assert.Throws<BridgeCallException>(() => PlanApprovalTestData.View(record).WorkbenchChanges(_en));
        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("zh-CN")]
    public void Dialog_MetadataEveryStepAndFirst50Changes_ContainOnlyVerifiedUndoWording(string language)
    {
        var culture = CultureInfo.GetCultureInfo(language);
        PlanApprovalView view = PlanApprovalTestData.View(PlanApprovalTestData.Record(60));
        ApprovalQuestion question = view.Question(["independent"], Eplan, PlanTestData.Created.AddMinutes(2));
        ApprovalAskArgs dialog = ApprovalTexts.For(question, culture);

        Assert.NotEmpty(dialog.Title);
        Assert.Contains(PlanApprovalTestData.ChangesPath, dialog.Summary, StringComparison.Ordinal);
        Assert.Contains(view.Record.PlanHash, dialog.Summary, StringComparison.Ordinal);
        Assert.Contains(dialog.Changes, l => l.StartsWith("Step 1:", StringComparison.Ordinal));
        Assert.Equal(50, dialog.Changes.Count(l => l.StartsWith("    ", StringComparison.Ordinal)));
        Assert.Contains(language == "zh-CN" ? "摘要，完整清单在文件。" : "Summary — full list in the file.", dialog.Changes);
        string all = dialog.Summary + dialog.Note + string.Join('\n', view.WorkbenchChanges(culture));
        Assert.DoesNotContain("Ctrl+Z", all, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(language == "zh-CN" ? "撤销只作用于 EPLAN 当前项目" : "Undo applies only to EPLAN's current project", all, StringComparison.Ordinal);
        Assert.Contains(language == "zh-CN" ? "第一步前会备份" : "A backup is made before the first step", all, StringComparison.Ordinal);
        Assert.Contains(language == "zh-CN" ? "撤销以已验证操作为限" : "undo is limited to verified operations", all, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\u202e")]
    [InlineData("\u2028")]
    [InlineData("\u00a0")]
    public void InvisibleProjectCharacters_AreVisibleEscapesInCompleteTextAndReview(string invisible)
    {
        PlanApprovalView view = PlanApprovalTestData.View(PlanApprovalTestData.Record(value: "before" + invisible + "after"));
        string full = PlanDiffPreview.For(view.Record.Definition.Steps);
        Assert.DoesNotContain("before" + invisible + "after", full, StringComparison.Ordinal);
        Assert.Contains("before\\u", full, StringComparison.Ordinal);
        Assert.Equal(view.Record.Definition.Digests.ChangesDigest, PlanStore.Hash(Encoding.UTF8.GetBytes(full)));
        Assert.Contains(view.WorkbenchChanges(_en), l => l.Contains("before\\u", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Path_Exactly400IsWhole_401IsRefusedInBothChannels(bool dialog)
    {
        PlanApprovalView view = PlanApprovalTestData.View() with { ChangesPath = "D:\\" + new string('a', 397) };
        Assert.Equal(400, view.ChangesPath.Length);
        Assert.Contains(view.ChangesPath, view.WorkbenchChanges(_en));
        Assert.Contains(view.ChangesPath, view.Dialog(_zh).Summary, StringComparison.Ordinal);
        view = view with { ChangesPath = view.ChangesPath + "a" };
        BridgeCallException error = Assert.Throws<BridgeCallException>(() => dialog ? view.Dialog(_en).Changes : view.WorkbenchChanges(_en));
        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
    }

    [Theory]
    [InlineData("relative/changes.txt")]
    [InlineData("D:\\review\u202e\\changes.txt")]
    [InlineData("D:\\review\n\\changes.txt")]
    public void Path_WhichCannotBeShownLiterally_IsRefused(string path)
    {
        PlanApprovalView view = PlanApprovalTestData.View() with { ChangesPath = path };
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeCallException>(() => view.WorkbenchChanges(_en)).Error.Reason);
    }

    [Fact]
    public void FrameLimit_RefusesTheActualSerializedV1MessageBeforeDiscovery()
    {
        ApprovalQuestion question = PlanApprovalTestData.View().Question(["f"], Eplan, PlanTestData.Created.AddMinutes(2));
        var channel = new WorkbenchChannel("unused", "unused", () => new string('x', WorkbenchProtocol.MaxFrameBytes), TimeProvider.System, NullLogger.Instance);
        BridgeCallException error = Assert.Throws<BridgeCallException>(() => channel.Ask(question, QuestionTier.Write, Eplan));
        Assert.Equal(ErrorReason.InvalidArgument, error.Error.Reason);
        var normal = new WorkbenchChannel("unused", "unused", () => "client", TimeProvider.System, NullLogger.Instance);
        byte[] bytes = Assert.IsType<byte[]>(normal.Ask(question, QuestionTier.Write, Eplan));
        ApprovalAskParams ask = WorkbenchJson.As<ApprovalAskParams>(WorkbenchJson.Read(bytes).Params);
        Assert.Equal(question.Plan.Fingerprint, ask.Fingerprint);
        Assert.Equal(PlanApprovalTestData.View().WorkbenchChanges(CultureInfo.CurrentUICulture), ask.Changes);
    }

    [Fact]
    public void Bindings_UseCanonicalObjectsAndOrderedFingerprints_WithFreshRandomRequestIds()
    {
        PlanApprovalView view = PlanApprovalTestData.View();
        ApprovalQuestion first = view.Question(["β", "a"], Eplan, PlanTestData.Created);
        ApprovalQuestion next = view.Question(["β", "a"], Eplan, PlanTestData.Created);
        string hash = view.Record.PlanHash;
        string id = view.Record.Definition.PlanId;

        Assert.Equal(PlanStore.Hash(Encoding.UTF8.GetBytes("{\"independentFingerprints\":[\"β\",\"a\"],\"planHash\":\"" + hash + "\"}")), first.Plan.Fingerprint);
        Assert.Equal(PlanStore.Hash(Encoding.UTF8.GetBytes("{\"planHash\":\"" + hash + "\",\"planId\":\"" + id + "\"}")), first.ArgumentsHash);
        Assert.NotEqual(first.RequestId, next.RequestId);
        Assert.Matches("^[a-f0-9]{32}$", first.RequestId);
        Assert.Equal(first.Plan.Fingerprint, next.Plan.Fingerprint);
        Assert.NotEqual(first.Plan.Fingerprint, view.Question(["a", "β"], Eplan, PlanTestData.Created).Plan.Fingerprint);
    }

    [Fact]
    public async Task BindingChecks_APreviousGrantCannotApproveANewPlanQuestion()
    {
        var clock = new ManualTime(PlanTestData.Created);
        ApprovalQuestion first = PlanApprovalTestData.View().Question(["f"], Eplan, clock.Now.AddMinutes(2));
        ApprovalQuestion next = PlanApprovalTestData.View().Question(["f"], Eplan, clock.Now.AddMinutes(2));
        ApprovalAnswer? granted = null;
        var channel = new FakeApprovalChannel("test", ApprovalDecision.Granted)
        {
            Bind = answer => granted ??= answer,
        };
        var service = new ApprovalService([channel], new HostConfig([], OutsideScratchWrites.Approve), clock, NullLogger<ApprovalService>.Instance);
        ApprovalResult firstResult = await service.AskAsync(first.RequestId, first.Tool, first.Tier, first.ArgumentsHash, first.Plan, Eplan, first.BackupFolder, null, null, TestContext.Current.CancellationToken);
        ApprovalResult replay = await service.AskAsync(next.RequestId, next.Tool, next.Tier, next.ArgumentsHash, next.Plan, Eplan, next.BackupFolder, null, null, TestContext.Current.CancellationToken);

        Assert.Equal(ApprovalDecision.Granted, firstResult.Decision);
        Assert.Equal(ApprovalDecision.Denied, replay.Decision);
    }
}
