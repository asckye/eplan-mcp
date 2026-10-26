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
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void TranslationSuspension_RequiresCheckingAndRestoringTheTranslationDatabase(bool zh, bool database)
    {
        var suspension = new Suspension(null, null, null, "eplan_translation_import", "r", DateTimeOffset.UnixEpoch,
            SuspensionReason.OutcomeUnknown, [], database ? @"$(md_translate)\Translate.db" : null);

        ApprovalAskArgs dialog = ApprovalTexts.For(_question with { Release = suspension }, CultureInfo.GetCultureInfo(zh ? "zh-CN" : "en-US"));

        Assert.Contains(zh ? "翻译库" : "translation database", dialog.Summary, StringComparison.Ordinal);
        Assert.Contains("Translate.db", dialog.Note, StringComparison.Ordinal);
        Assert.Contains(zh ? "关闭 EPLAN" : "close EPLAN", dialog.Note, StringComparison.Ordinal);
        Assert.DoesNotContain(zh ? "部件" : "parts", dialog.Summary + dialog.Note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void TranslationImport_NamesTheDatabaseAndAdditiveEffect_WithTenTruncatedEntries(bool zh)
    {
        var plan = new WritePlan
        {
            MasterData = new MasterDataTarget { Kind = MasterDataKinds.Translations, Database = @"$(md_translate)\Translate.db", Path = @"C:\Translations\Translate.db" },
            Changes = [new PlannedChange { Action = "update", Kind = "translation", Name = "glossary.txt" }],
            Details = new Dictionary<string, string>
            {
                ["file"] = "glossary.txt",
                ["bytes"] = "1234",
                ["sha256"] = "0123456789abcdef",
                ["entries"] = "11",
                ["referenceLanguage"] = "en_US",
                ["languages"] = "zh_CN",
                ["preview"] = System.Text.Json.JsonSerializer.Serialize(Enumerable.Range(0, 11).Select(i => new Dictionary<string, string>
                {
                    ["en_US"] = new string('x', 41) + i,
                    ["zh_CN"] = "电机\n\u202e批准",
                })),
            },
        };

        ApprovalAskArgs dialog = ApprovalTexts.For(_question with { Plan = plan, Tool = "eplan_translation_import" }, CultureInfo.GetCultureInfo(zh ? "zh-CN" : "en-US"));

        Assert.Contains(zh ? "翻译库" : "translation database", dialog.Title, StringComparison.Ordinal);
        Assert.Contains(plan.MasterData.Database, dialog.Summary, StringComparison.Ordinal);
        Assert.Contains(plan.MasterData.Path, dialog.Summary, StringComparison.Ordinal);
        Assert.Contains(zh ? "影响使用这个库的所有项目" : "Every project that uses this translation database is affected", dialog.Summary, StringComparison.Ordinal);
        string changes = string.Join("\n", dialog.Changes);
        Assert.Contains("glossary.txt; 1234; 0123456789abcdef", changes, StringComparison.Ordinal);
        Assert.Contains("11; en_US; zh_CN", changes, StringComparison.Ordinal);
        Assert.Contains(zh ? "只新增词条、补上空缺译文，已有译文不会被改写" : "adds entries and fills empty translations; existing translations are never rewritten", changes, StringComparison.Ordinal);
        Assert.Equal(10, dialog.Changes.Count(c => c.StartsWith("en_US: ", StringComparison.Ordinal)));
        Assert.Contains(new string('x', 40), changes, StringComparison.Ordinal);
        Assert.DoesNotContain(new string('x', 41), changes, StringComparison.Ordinal);
        Assert.DoesNotContain('\u202e', changes);
        Assert.Contains(zh ? "导入撤不回，执行前会备份翻译库" : "cannot be undone, the translation database is backed up first", dialog.Note, StringComparison.Ordinal);
        Assert.DoesNotContain(zh ? "部件" : "parts", changes + dialog.Note, StringComparison.Ordinal);
    }

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

    private static readonly ApprovalQuestion _question = new("r", "eplan_page_create", "h", _plan, [], DateTimeOffset.UnixEpoch) { Tier = ToolTier.Write, Eplan = null };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CableGeneration_SchemesStayInert_AndUndoIsNotPromised(bool zh)
    {
        WritePlan plan = new()
        {
            Project = "Line1",
            ProjectPath = _plan.ProjectPath,
            Changes = [new PlannedChange { Action = "update", Kind = "cables", Name = "Line1", Property = "generation" }],
            Details = new Dictionary<string, string>
            {
                ["creationScheme"] = "Standard\n\u202eApproved",
                ["numberingScheme"] = "Standard",
                ["selectionScheme"] = "Standard",
                ["before"] = "{\"cables\":78,\"automatic\":0,\"automaticUnknown\":78,\"connections\":12}",
                ["cables"] = "[{\"name\":\"+B1-WG2\\n\\u202eApproved\",\"automatic\":null}]",
            },
        };
        ApprovalAskArgs texts = ApprovalTexts.For(_question with { Tool = "eplan_cable_generate", Plan = plan }, CultureInfo.GetCultureInfo(zh ? "zh-CN" : "en-US"));

        Assert.All(texts.Changes, line =>
        {
            Assert.DoesNotContain('\n', line);
            Assert.DoesNotContain('\u202e', line);
        });
        Assert.Contains("+B1-WG2 Approved; automatic=null", texts.Changes);
        Assert.DoesNotContain("Ctrl+Z", texts.Note, StringComparison.Ordinal);
        Assert.Contains(zh ? "尚未验证" : "not been verified", texts.Note, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("en-US", "Import the parts list", "Undo after commit has not been verified")]
    [InlineData("zh-CN", "导入项目的 parts 清单", "提交后的撤销尚未验证")]
    public void ListImport_UsesProjectScope_FullHashAndUnverifiedUndo(string culture, string change, string note)
    {
        var plan = new WritePlan
        {
            Project = "Line1",
            Changes = [new PlannedChange { Action = "update", Kind = "parts", Property = "list" }],
            Details = new() { ["file"] = "incoming/parts\u202e\n.xml", ["bytes"] = "14000000", ["sha256"] = new string('a', 64), ["converter"] = "XPalXmlExporter", ["before"] = "293 parts, 2900 references" },
        };
        ApprovalAskArgs text = ApprovalTexts.For(_question with { Tool = "eplan_import_list", Plan = plan }, CultureInfo.GetCultureInfo(culture));
        Assert.Contains(change, text.Changes[0], StringComparison.Ordinal);
        Assert.Contains(new string('a', 64), text.Changes[0], StringComparison.Ordinal);
        Assert.Contains(note, text.Note, StringComparison.Ordinal);
        Assert.DoesNotContain('\u202e', text.Changes[0]);
        Assert.DoesNotContain('\n', text.Changes[0]);
        Assert.DoesNotContain("parts the database has", text.Changes[0], StringComparison.Ordinal);
    }

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

    [Theory]
    [InlineData("zh-CN", "执行前会先把整个项目备份到 C:\\Data\\backups\\Line1\\ 下的新文件夹。批准后可在 EPLAN 中按 Ctrl+Z 撤销“AI: eplan_page_delete”这一步。")]
    [InlineData("en-US", "Before the change, the whole project is backed up to a new folder in C:\\Data\\backups\\Line1\\. After approval, Ctrl+Z in EPLAN undoes the step \"AI: eplan_page_delete\".")]
    public void ADestructiveWrite_NamesWhereTheProjectIsBackedUpFirst(string culture, string note)
    {
        ApprovalQuestion question = _question with { Tool = "eplan_page_delete", BackupFolder = @"C:\Data\backups\Line1" };

        Assert.Equal(note, ApprovalTexts.For(question, CultureInfo.GetCultureInfo(culture)).Note);
    }

    [Theory]
    [InlineData("zh-CN", "删除页 =GAA+A1&EFS1/1（Circuit, 59 functions）", "删除后这些设备在其他页上的功能将失去主功能：", "+A1-WE1：=GAA+A1&EFS1/2、=GAA+A1&EFS1/3 上的 6 个功能", "……共 51 个设备")]
    [InlineData("en-US", "Delete page =GAA+A1&EFS1/1 (Circuit, 59 functions)", "After the deletion, these devices keep functions on other pages without their main function:", "+A1-WE1: 6 functions on =GAA+A1&EFS1/2, =GAA+A1&EFS1/3", "… 51 devices in all")]
    public void APageDeletion_ListsTheDevicesLeftWithoutTheirMainFunctionAfterTheChanges(string culture, string change, string heading, string device, string total)
    {
        WritePlan plan = new()
        {
            Project = "Line1",
            ProjectPath = @"D:\Projects\Line1",
            Changes = [new PlannedChange { Action = "delete", Kind = "page", Name = "=GAA+A1&EFS1/1", OldValue = "Circuit, 59 functions" }],
            Details = new Dictionary<string, string>
            {
                ["orphaned"] = "+A1-WE1\t6\t=GAA+A1&EFS1/2\t=GAA+A1&EFS1/3\n+A2-WE1\t1\t=GAA+A1&EFS1/2",
                ["orphanedCount"] = "51",
            },
        };

        ApprovalAskArgs texts = ApprovalTexts.For(_question with { Plan = plan }, CultureInfo.GetCultureInfo(culture));

        Assert.Equal([change, string.Empty, heading, device], texts.Changes.Take(4));
        Assert.Equal((6, total), (texts.Changes.Count, texts.Changes[^1]));
    }

    [Theory]
    [InlineData("zh-CN", "重新编号：device tags containing '?' on 1 page (51 devices), scheme: EPLAN default, start 1, step 1", "要编号的设备：", "……共 51 个设备")]
    [InlineData("en-US", "Renumber device tags containing '?' on 1 page (51 devices), scheme: EPLAN default, start 1, step 1", "Devices to number:", "… 51 devices in all")]
    public void ADeviceRenumbering_ListsTheDevicesToNumberAfterTheScope(string culture, string change, string heading, string total)
    {
        WritePlan plan = new()
        {
            Changes = [new PlannedChange { Action = "update", Kind = "devices", Name = "Line1", Property = "device tags", OldValue = "device tags containing '?' on 1 page (51 devices), scheme: EPLAN default, start 1, step 1" }],
            Details = new Dictionary<string, string> { ["devices"] = "=GAA+A1-?QA1\n+A1-?XG1", ["deviceCount"] = "51" },
        };

        ApprovalAskArgs texts = ApprovalTexts.For(_question with { Plan = plan }, CultureInfo.GetCultureInfo(culture));

        Assert.Equal([change, string.Empty, heading, "=GAA+A1-?QA1", "+A1-?XG1", total], texts.Changes);
    }

    [Theory]
    [InlineData("zh-CN", "重新编号页 =A/90", "编号：from 200, step 10, per structure, subpages: retain, keep text: no")]
    [InlineData("en-US", "Renumber page =A/90", "Numbering: from 200, step 10, per structure, subpages: retain, keep text: no")]
    public void APageRenumbering_ListsThePagesThenTheNumbering(string culture, string change, string numbering)
    {
        WritePlan plan = new()
        {
            Changes = [new PlannedChange { Action = "update", Kind = "page", Name = "=A/90", Property = "page number" }],
            Details = new Dictionary<string, string> { ["numbering"] = "from 200, step 10, per structure, subpages: retain, keep text: no" },
        };

        Assert.Equal([change, string.Empty, numbering], ApprovalTexts.For(_question with { Plan = plan }, CultureInfo.GetCultureInfo(culture)).Changes);
    }

    [Theory]
    [InlineData("zh-CN", "修改项目 Line1 的语言：'display: ##_##' → 'display: en_US, zh_CN'", "Ctrl+Z 不能撤销语言设置；要恢复，用原来的值再设置一次。")]
    [InlineData("en-US", "Change the languages of project Line1: 'display: ##_##' → 'display: en_US, zh_CN'", "Ctrl+Z in EPLAN does not undo language settings; to restore them, set the old values again.")]
    public void ALanguageChange_IsOneLineAndSaysCtrlZDoesNotUndoIt(string culture, string change, string note)
    {
        WritePlan plan = new()
        {
            Changes = [new PlannedChange { Action = "update", Kind = "project", Name = "Line1", Property = "languages", OldValue = "display: ##_##", NewValue = "display: en_US, zh_CN" }],
        };

        ApprovalAskArgs texts = ApprovalTexts.For(_question with { Tool = "eplan_language_set", Plan = plan }, CultureInfo.GetCultureInfo(culture));

        Assert.Equal([change], texts.Changes);
        Assert.Equal(note, texts.Note);
    }

    [Theory]
    [InlineData("zh-CN", "删除语言 sv_SE（460 translations）", "项目文本中这些语言的全部译文都将删除（共 460 个文本）；EPLAN 可能同时修改显示语言。", "执行前会先把整个项目备份到 C:\\Data\\backups\\Line1\\ 下的新文件夹。批准后可在 EPLAN 中按 Ctrl+Z 撤销“AI: eplan_language_remove”这一步。语言设置可能不随之恢复，备份中有原样的项目。")]
    [InlineData("en-US", "Delete language sv_SE (460 translations)", "All translations in these languages are deleted from the project's texts (460 texts); EPLAN may change the displayed languages too.", "Before the change, the whole project is backed up to a new folder in C:\\Data\\backups\\Line1\\. After approval, Ctrl+Z in EPLAN undoes the step \"AI: eplan_language_remove\". The language settings may not come back with it; the backup has the project as it was.")]
    public void ALanguageRemoval_SaysTheTranslationsGoAndTheSettingsMayNotComeBack(string culture, string change, string translations, string note)
    {
        WritePlan plan = new()
        {
            Changes = [new PlannedChange { Action = "delete", Kind = "language", Name = "sv_SE", OldValue = "460 translations" }],
            Details = new Dictionary<string, string> { ["textsAffected"] = "460", ["translation"] = "de_DE\nsv_SE", ["display"] = "##_##" },
        };

        ApprovalAskArgs texts = ApprovalTexts.For(_question with { Tool = "eplan_language_remove", Plan = plan, BackupFolder = @"C:\Data\backups\Line1" }, CultureInfo.GetCultureInfo(culture));

        Assert.Equal([change, string.Empty, translations], texts.Changes);
        Assert.Equal(note, texts.Note);
    }

    [Theory]
    [InlineData("zh-CN", "重新编号：all terminal designations on 1 strip (12 terminals), scheme: Numerisch, start 1, step 1")]
    [InlineData("en-US", "Renumber all terminal designations on 1 strip (12 terminals), scheme: Numerisch, start 1, step 1")]
    public void ANumbering_ShowsTheScopeAsTheBridgeWordsIt(string culture, string change)
    {
        WritePlan plan = new()
        {
            Changes = [new PlannedChange { Action = "update", Kind = "terminals", Name = "Line1", Property = "numbering", OldValue = "all terminal designations on 1 strip (12 terminals), scheme: Numerisch, start 1, step 1" }],
            Details = new Dictionary<string, string> { ["terminalCount"] = "12", ["stripCount"] = "1" },
        };

        Assert.Equal([change], ApprovalTexts.For(_question with { Plan = plan }, CultureInfo.GetCultureInfo(culture)).Changes);
        Assert.Equal(["renumber all terminal designations on 1 strip (12 terminals), scheme: Numerisch, start 1, step 1"], WritePipeline.ChangeLines(plan));
    }

    [Fact]
    public void AMacroInsertsDevices_AreNotListedAsDevicesToNumber()
    {
        WritePlan plan = new()
        {
            Changes = [new PlannedChange { Action = "create", Kind = "macro", Name = "=A/1", NewValue = "m.ema" }],
            Details = new Dictionary<string, string> { ["devices"] = "+-WG6\t-WG6\tCable", ["deviceCount"] = "1" },
        };

        Assert.Single(ApprovalTexts.For(_question with { Plan = plan }, CultureInfo.GetCultureInfo("en-US")).Changes);
    }

    [Fact]
    public void APlanWithoutDevicesLeftBehind_HasNoSuchSection()
    {
        WritePlan plan = new() { Changes = [new PlannedChange { Action = "delete", Kind = "page", Name = "=A/1" }], Details = new Dictionary<string, string> { ["orphaned"] = string.Empty, ["orphanedCount"] = "0" } };

        Assert.Equal(["Delete page =A/1"], ApprovalTexts.For(_question with { Plan = plan }, CultureInfo.GetCultureInfo("en-US")).Changes);
    }

    [Theory]
    [InlineData(true, "在页 =GAA+A1&EFS1/60 上插入宏：Contactor_Feedback_Control.ema at (40, 280), numbering number, 8 devices")]
    [InlineData(false, "Insert a macro on page =GAA+A1&EFS1/60: Contactor_Feedback_Control.ema at (40, 280), numbering number, 8 devices")]
    public void AMacroInsert_ShowsWhatGoesWhere(bool zh, string line)
    {
        var change = new PlannedChange { Action = "create", Kind = "macro", Name = "=GAA+A1&EFS1/60", NewValue = "Contactor_Feedback_Control.ema at (40, 280), numbering number, 8 devices" };

        Assert.Equal(line, ApprovalTexts.Change(change, zh));
    }

    [Theory]
    [InlineData(true, "在页 =GAA+A1&EFS1/60 上放置设备：SIE.3RH2122-1HB40 as =GAA+A1-KF20 at (80, 160)")]
    [InlineData(false, "Place a device on page =GAA+A1&EFS1/60: SIE.3RH2122-1HB40 as =GAA+A1-KF20 at (80, 160)")]
    public void ADevicePlacement_ShowsThePartTheTagAndThePoint(bool zh, string line)
    {
        var change = new PlannedChange { Action = "create", Kind = "device", Name = "=GAA+A1&EFS1/60", NewValue = "SIE.3RH2122-1HB40 as =GAA+A1-KF20 at (80, 160)" };

        Assert.Equal(line, ApprovalTexts.Change(change, zh));
    }

    [Theory]
    [InlineData(true, "在页 =GAA+A1&EFS1/92 上导入图形：4685200F.DXF (25 KB, sha256 1a2b3c4d5e6f…), scale fit, center")]
    [InlineData(false, "Import a drawing on page =GAA+A1&EFS1/92: 4685200F.DXF (25 KB, sha256 1a2b3c4d5e6f…), scale fit, center")]
    public void ADrawingImport_ShowsTheFileItsSizeAndSha256(bool zh, string line)
    {
        var change = new PlannedChange { Action = "create", Kind = "drawing", Name = "=GAA+A1&EFS1/92", NewValue = "4685200F.DXF (25 KB, sha256 1a2b3c4d5e6f…), scale fit, center" };

        Assert.Equal(line, ApprovalTexts.Change(change, zh));
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

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("en-US")]
    public void AMasterDataWrite_NamesTheDatabase_EveryProject_TheBackup_AndNoUndo(string culture)
    {
        // ADR-0019: the database's type and path, "every project using it", and the master-data backup; no project.
        var plan = new WritePlan
        {
            Changes = [new PlannedChange { Action = "create", Kind = "part", Name = "added", Property = "parts", NewValue = "3" }],
            Fingerprint = "f",
            MasterData = new MasterDataTarget { Kind = MasterDataKinds.Parts, Database = @"$(MD_PARTS)\EPLAN_parts.alk", Path = @"C:\Parts\EPLAN_parts.alk" },
        };
        ApprovalQuestion question = _question with { Plan = plan, Tool = "eplan_part_import", Tier = ToolTier.Destructive, MasterDataBackupFolder = @"C:\Data\backups\masterdata" };

        ApprovalAskArgs texts = ApprovalTexts.For(question, CultureInfo.GetCultureInfo(culture));

        if (culture == "zh-CN")
        {
            Assert.Equal("批准 AI 修改 EPLAN 部件库？", texts.Title);
            Assert.Equal("工具：eplan_part_import\n部件库：$(MD_PARTS)\\EPLAN_parts.alk（文件型，C:\\Parts\\EPLAN_parts.alk）\n这个部件库被所有项目共用：修改影响使用它的所有项目。\n将进行以下修改（共 1 处）：", texts.Summary);
            Assert.Equal("执行前会先把部件库备份到 C:\\Data\\backups\\masterdata\\ 下的新文件夹。部件库中的修改撤不回，Ctrl+Z 也不能撤销；需要时按备份恢复。", texts.Note);
        }
        else
        {
            Assert.Equal("Approve AI changes to EPLAN's parts database?", texts.Title);
            Assert.Equal("Tool: eplan_part_import\nParts database: $(MD_PARTS)\\EPLAN_parts.alk (file database, C:\\Parts\\EPLAN_parts.alk)\nEvery project that uses this parts database is affected.\nThe following changes will be made (1):", texts.Summary);
            Assert.Equal("Before the change, the parts database is backed up to a new folder in C:\\Data\\backups\\masterdata\\. Changes to the parts database cannot be undone, not even with Ctrl+Z. If needed, restore it from its backup.", texts.Note);
        }
    }

    [Fact]
    public void AMasterDataWrite_ThatChangesAProject_NamesBoth_AndBothBackups()
    {
        var plan = new WritePlan
        {
            Project = _plan.Project,
            ProjectPath = _plan.ProjectPath,
            Changes = _plan.Changes,
            Fingerprint = "f",
            MasterData = new MasterDataTarget { Kind = MasterDataKinds.Parts, Database = @"$(MD_PARTS)\EPLAN_parts.alk" },
        };
        ApprovalQuestion question = _question with { Plan = plan, Tool = "eplan_part_sync", BackupFolder = @"C:\Data\backups\Line1", MasterDataBackupFolder = @"C:\Data\backups\masterdata" };

        ApprovalAskArgs texts = ApprovalTexts.For(question, CultureInfo.GetCultureInfo("zh-CN"));

        Assert.Equal("批准 AI 修改 EPLAN 部件库和项目？", texts.Title);
        Assert.StartsWith("工具：eplan_part_sync\n部件库：$(MD_PARTS)\\EPLAN_parts.alk（文件型）\n这个部件库被所有项目共用：修改影响使用它的所有项目。\n项目：Line1（D:\\Projects\\Line1）\n", texts.Summary, StringComparison.Ordinal);
        Assert.Equal("执行前会先把整个项目备份到 C:\\Data\\backups\\Line1\\ 下的新文件夹。执行前会先把部件库备份到 C:\\Data\\backups\\masterdata\\ 下的新文件夹。部件库中的修改撤不回：Ctrl+Z 只撤销项目中的修改；需要时按备份恢复部件库。", texts.Note);
    }

    [Fact]
    public void TheReleaseConfirmation_ForADatabase_SaysToCheckIt()
    {
        var suspension = new Suspension(null, null, null, "eplan_part_import", "r1", DateTimeOffset.MinValue, SuspensionReason.OutcomeUnknown, ["add 3 parts"], @"$(MD_PARTS)\EPLAN_parts.alk");

        ApprovalAskArgs texts = ApprovalTexts.For(_question with { Release = suspension }, CultureInfo.GetCultureInfo("en-US"));

        Assert.Equal("Time: ?\nTool: eplan_part_import\nParts database: $(MD_PARTS)\\EPLAN_parts.alk\nThis change was interrupted and whether it was made is unknown; the parts database may contain part of it. AI writes are suspended. Planned changes (up to 20):", texts.Summary);
        Assert.StartsWith("Check the parts database in EPLAN's parts management first; if needed, close EPLAN and restore the database", texts.Note, StringComparison.Ordinal);
    }

    [Fact]
    public void APartsImport_ListsTheFile_TheFirst20PartsOfEachKind_AndTheRest()
    {
        var plan = new WritePlan
        {
            Changes =
            [
                new PlannedChange { Action = "create", Kind = "parts", Name = "supplier/relays.xml", Property = "parts", NewValue = "25" },
                new PlannedChange { Action = "update", Kind = "parts", Name = "supplier/relays.xml", Property = "parts", NewValue = "1" },
            ],
            Fingerprint = "f",
            MasterData = new MasterDataTarget { Kind = MasterDataKinds.Parts, Database = @"$(MD_PARTS)\EPLAN_parts.alk" },
            Details = new Dictionary<string, string>
            {
                ["file"] = "relays.xml (12 KB, sha256 0123456789ab…)",
                ["added"] = string.Join("\n", Enumerable.Range(1, 25).Select(i => "P" + i)),
                ["addedCount"] = "25",
                ["updated"] = "SIE.3RH2122-1HB40",
                ["updatedCount"] = "1",
                ["skippedCount"] = "3",
                ["mode"] = "update_and_append",
                ["matchBy"] = "part_number",
            },
        };

        ApprovalAskArgs texts = ApprovalTexts.For(_question with { Plan = plan, Tool = "eplan_part_import" }, CultureInfo.GetCultureInfo("zh-CN"));

        Assert.Equal(
            [
                "从 supplier/relays.xml 新增 25 个部件",
                "从 supplier/relays.xml 更新部件库中已有的 1 个部件",
                string.Empty,
                "文件：relays.xml (12 KB, sha256 0123456789ab…)",
                "新增的部件：",
                .. Enumerable.Range(1, 20).Select(i => "P" + i),
                "……共 25 个",
                "更新的部件：",
                "SIE.3RH2122-1HB40",
                "不变的部件：3 个（方式 update_and_append，按 part_number 判断是否已有）",
            ],
            texts.Changes);
        Assert.Equal(["add 25 parts from supplier/relays.xml to the parts database", "update 1 part of the parts database from supplier/relays.xml"], WritePipeline.ChangeLines(plan));
    }

    [Fact]
    public void APartsSynchronization_SaysTheProjectChangesToo_AndListsWhatItStores()
    {
        var plan = new WritePlan
        {
            Project = "Line1",
            ProjectPath = @"D:\Projects\Line1",
            Changes = [new PlannedChange { Action = "update", Kind = "project", Name = "Line1", Property = "stored parts", OldValue = "293", NewValue = "1" }],
            Fingerprint = "f",
            MasterData = new MasterDataTarget { Kind = MasterDataKinds.Parts, Database = @"$(MD_PARTS)\EPLAN_parts.alk" },
            Details = new Dictionary<string, string> { ["added"] = string.Empty, ["updated"] = string.Empty, ["stored"] = "PXC.3209510", ["storedCount"] = "1", ["skippedCount"] = "293", ["mode"] = "append_new" },
        };

        ApprovalAskArgs texts = ApprovalTexts.For(_question with { Plan = plan, Tool = "eplan_part_sync" }, CultureInfo.GetCultureInfo("zh-CN"));

        Assert.Equal(
            ["项目也会被修改：在项目 Line1 中存入 1 个被引用的部件（现有 293 个）", string.Empty, "存入项目的部件：", "PXC.3209510", "部件库中不变的部件：293 个（方式 append_new）"],
            texts.Changes);
        Assert.Equal(["store 1 part the project references in project Line1 (now 293)"], WritePipeline.ChangeLines(plan));
    }

    [Fact]
    public void UnknownKindsAndProperties_AreShownAsTheyAre()
    {
        var change = new PlannedChange { Action = "update", Kind = "cable", Name = "-W1", Property = "length", OldValue = "1", NewValue = "2" };

        Assert.Equal("修改cable -W1 的length：'1' → '2'", ApprovalTexts.Change(change, zh: true));
    }
}
