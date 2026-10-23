using System.Text;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tests.Plans;

[Trait("Size", "Small")]
public sealed class PlanArgumentsTests
{
    private static readonly string[] _onePage = ["+A/1"];
    internal static PlanArgumentCheck Check(params object[] steps) => PlanArguments.Check(
        JsonSerializer.SerializeToElement(new { project = "Demo", steps }), "en_US", "IEC");
    internal static object Step(string op, object args, params int[] depends) => new { op, args, depends_on = depends };
    internal static object[] Pages(int count) => [.. Enumerable.Range(1, count).Select(i => (object)new { name = "+A/" + i, description = "Power" })];

    [Fact]
    public void Catalog_OffersExactlySevenOperationsWithServerTiers()
    {
        string[] ops = ["page.create", "page.update", "device.update", "macro.insert", "page.delete", "page.renumber", "device.renumber"];
        JsonElement branches = PlanStepCatalog.InputSchema.GetProperty("properties").GetProperty("steps").GetProperty("items").GetProperty("oneOf");
        Assert.Equal(ops, branches.EnumerateArray().Select(b => b.GetProperty("properties").GetProperty("op").GetProperty("const").GetString()));
        foreach (string op in ops)
        { Assert.Equal(op is "page.delete" or "page.renumber" or "device.renumber" ? ToolTier.Destructive : ToolTier.Write, PlanStepCatalog.Tier(op)); }
    }

    [Theory]
    [InlineData("module.insert")]
    [InlineData("placeholder.apply")]
    [InlineData("project.create")]
    [InlineData("project.restore")]
    [InlineData("device.place")]
    [InlineData("connection.generate")]
    [InlineData("report.update")]
    [InlineData("action.run")]
    [InlineData("eplan_page_create")]
    [InlineData("arbitrary")]
    public void Catalog_RejectsEveryUnofferedOperation(string op)
    {
        PlanArgumentCheck result = Check(Step(op, new { }));
        Assert.Equal(ErrorReason.InvalidArgument, result.Reason);
        Assert.Equal("/steps/0/op", Assert.Single(result.Violations).Pointer);
        Assert.Contains("not offered", result.Violations[0].Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("project")]
    [InlineData("dry_run")]
    [InlineData("fingerprint")]
    [InlineData("approval")]
    [InlineData("backup")]
    [InlineData("undo")]
    [InlineData("selection")]
    [InlineData("tier")]
    [InlineData("x~/y")]
    [InlineData("x'~/y")]
    public void OverridesAndUnknownFields_HaveEscapedPointers(string field)
    {
        PlanArgumentCheck result = Check(Step("device.update", new Dictionary<string, object> { ["tag"] = "-K1", ["new_tag"] = "-K2", [field] = true }));
        Assert.Equal("/steps/0/args/" + field.Replace("~", "~0", StringComparison.Ordinal).Replace("/", "~1", StringComparison.Ordinal), Assert.Single(result.Violations).Pointer);
        Assert.Equal(result.Violations[0].Pointer, result.Metadata["pointer"]!.GetValue<string>());
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(20, true)]
    [InlineData(21, false)]
    public void StepCount_IsBounded(int count, bool valid)
    {
        PlanArgumentCheck result = Check([.. Enumerable.Repeat(Step("device.update", new { tag = "-K1", new_tag = "-K2" }), count)]);
        Assert.Equal(valid, result.Reason is null);
        if (!valid)
        { Assert.Contains(result.Violations, v => v.Pointer == "/steps"); }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(50, true)]
    [InlineData(51, false)]
    public void PageCounts_ReuseToolLimits(int count, bool valid)
    {
        foreach (string op in new[] { "page.create", "page.update", "page.delete", "page.renumber", "device.renumber" })
        {
            object pages = op is "page.create" or "page.update" ? Pages(count) : Enumerable.Range(1, count).Select(i => "+A/" + i).ToArray();
            var args = new Dictionary<string, object> { ["pages"] = pages };
            if (op == "page.renumber")
            { args["start"] = 1; }
            PlanArgumentCheck result = Check(Step(op, args));
            Assert.Equal(valid, result.Reason is null);
            if (!valid)
            { Assert.Contains(result.Violations, v => v.Pointer == "/steps/0/args/pages"); }
        }
    }

    [Theory]
    [InlineData(262143, true)]
    [InlineData(262144, true)]
    [InlineData(262145, false)]
    public void ArgsBytes_CountRawUtf8IncludingUnicodeAndWhitespace(int bytes, bool valid)
    {
        const string args = """{"pages":[{"name":"+A/1","description":"电源"}]}""";
        string padded = args.Insert(args.Length - 1, new string(' ', bytes - Encoding.UTF8.GetByteCount(args)));
        string json = "{\"project\":\"Demo\",\"steps\":[{\"op\":\"page.create\",\"args\":" + padded + "}]}";
        PlanArgumentCheck result = PlanArguments.Check(PlanTestData.Json(json), "en_US", "IEC");
        Assert.Equal(valid, result.Reason is null);
        if (!valid)
        { Assert.Contains(result.Violations, v => v.Message.Contains("256 KiB", StringComparison.Ordinal)); }
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(5, false)]
    public void ExplicitLocators_AreBoundedAcrossSteps(int count, bool valid)
    {
        PlanArgumentCheck result = Check([.. Enumerable.Repeat(Step("page.update", new { pages = Pages(50) }), count)]);
        Assert.Equal(valid, result.Reason is null);
        if (!valid)
        { Assert.Contains(result.Violations, v => v.Message.Contains("200", StringComparison.Ordinal)); }
    }

    [Fact]
    public void AllSeven_DefaultsAreExplicitAndContextIsFrozen()
    {
        PlanArgumentCheck result = Check(
            Step("page.create", new { pages = new[] { new { name = "+A/1" } } }),
            Step("page.update", new { pages = Pages(1) }),
            Step("device.update", new { tag = "-K1", new_tag = "-K2" }),
            Step("macro.insert", new { page = "+A/1", macro = "Folder/Motor.ema", x = 40, y = 100 }),
            Step("page.delete", new { pages = _onePage }),
            Step("page.renumber", new { pages = _onePage, start = 10 }),
            Step("device.renumber", new { pages = _onePage }));
        Assert.Empty(result.Violations);
        Assert.Equal("en_US", result.Steps[0].Args.GetProperty("language").GetString());
        Assert.Equal("en_US", result.Steps[1].Args.GetProperty("language").GetString());
        JsonElement page = result.Steps[0].Args.GetProperty("pages")[0];
        Assert.Equal("Circuit", page.GetProperty("page_type").GetString());
        Assert.Equal(JsonValueKind.Null, page.GetProperty("description").ValueKind);
        Assert.Equal(0, result.Steps[3].Args.GetProperty("variant").GetInt32());
        Assert.Equal("keep", result.Steps[3].Args.GetProperty("numbering").GetString());
        Assert.Equal("retain", result.Steps[5].Args.GetProperty("subpages").GetString());
        Assert.False(result.Steps[5].Args.GetProperty("keep_text").GetBoolean());
        Assert.Equal(1, result.Steps[5].Args.GetProperty("step").GetInt32());
        Assert.Equal("IEC", result.Steps[6].Args.GetProperty("scheme").GetString());
        Assert.Equal(1, result.Steps[6].Args.GetProperty("start").GetInt32());
        Assert.Equal(1, result.Steps[6].Args.GetProperty("step").GetInt32());
        Assert.True(result.Steps[6].Args.GetProperty("only_unnumbered").GetBoolean());
        Assert.All(result.Steps, s => { Assert.Empty(s.DependsOn); Assert.False(s.Args.TryGetProperty("project", out _)); });
    }

    [Fact]
    public void ExplicitLanguageAndScheme_ArePreserved()
    {
        PlanArgumentCheck result = Check(Step("page.update", new { pages = Pages(1), language = "zh_CN" }),
            Step("device.renumber", new { pages = _onePage, scheme = "DIN", only_unnumbered = false, start = 10, step = 2 }));
        Assert.Empty(result.Violations);
        Assert.Equal("zh_CN", result.Steps[0].Args.GetProperty("language").GetString());
        Assert.Equal("DIN", result.Steps[1].Args.GetProperty("scheme").GetString());
    }

    [Fact]
    public void DeviceRenumber_RequiresExplicitPages()
    { Assert.Contains(Check(Step("device.renumber", new { })).Violations, v => v.Pointer == "/steps/0/args/pages"); }

    [Theory]
    [InlineData("page.renumber", 0, 1, "start")]
    [InlineData("page.renumber", 1, 0, "step")]
    [InlineData("device.renumber", -1, 1, "start")]
    [InlineData("device.renumber", 1, -1, "step")]
    public void RenumberNumbers_ReuseToolRulesAtTheInvalidField(string op, int start, int step, string field)
    { Assert.Contains(Check(Step(op, new { pages = _onePage, start, step })).Violations, v => v.Pointer == "/steps/0/args/" + field); }

    [Theory]
    [InlineData("page.create")]
    [InlineData("page.update")]
    public void OmittedAndNullLanguage_AreFrozenToCompilationContext(string op)
    {
        PlanArgumentCheck result = Check(Step(op, new { pages = Pages(1), language = (string?)null }));
        Assert.Empty(result.Violations);
        Assert.Equal("en_US", result.Steps[0].Args.GetProperty("language").GetString());
    }

    [Theory]
    [InlineData("Motor.emp", 0, "keep", "macro")]
    [InlineData("../Motor.ema", 0, "keep", "macro")]
    [InlineData("C:/Motor.ema", 0, "keep", "macro")]
    [InlineData("/Motor.ema", 0, "keep", "macro")]
    [InlineData("$(MD_MACROS)/Motor.ema", 0, "keep", "macro")]
    [InlineData("Folder/Motor.ema", 1, "keep", "variant")]
    [InlineData("Folder/Motor.ema", 0, "number", "numbering")]
    public void MacroRestrictions_HaveFieldPointers(string macro, int variant, string numbering, string field)
    { Assert.Contains(Check(Step("macro.insert", new { page = "+A/1", macro, x = 1, y = 2, variant, numbering })).Violations, v => v.Pointer == "/steps/0/args/" + field); }

    [Fact]
    public void ShapeAndSemanticErrors_AreCollectedAcrossFieldsAndSteps()
    {
        PlanArgumentCheck result = Check(Step("page.update", new { pages = Array.Empty<object>(), language = "bad", approval = true }),
            Step("module.insert", new { }), Step("page.create", new { pages = new object[] { new { name = "+A/1", bad = true }, "bad", new { description = "missing name" } } }));
        Assert.Equal(ErrorReason.InvalidArgument, result.Reason);
        Assert.Contains(result.Violations, v => v.Pointer == "/steps/0/args/pages");
        Assert.Contains(result.Violations, v => v.Pointer == "/steps/0/args/language");
        Assert.Contains(result.Violations, v => v.Pointer == "/steps/0/args/approval");
        Assert.Contains(result.Violations, v => v.Pointer == "/steps/1/op");
        Assert.Contains(result.Violations, v => v.Pointer == "/steps/2/args/pages/0/bad");
        Assert.Contains(result.Violations, v => v.Pointer == "/steps/2/args/pages/1");
        Assert.Contains(result.Violations, v => v.Pointer == "/steps/2/args/pages/2/name");
        Assert.Equal(result.Violations.Length, result.Metadata["violations"]!.AsArray().Count);
        Assert.False(result.Metadata.ContainsKey("pointer"));
    }

    [Fact]
    public void MacroSchemaErrors_DoNotHideOtherRestrictions()
    {
        PlanArgumentCheck result = Check(Step("macro.insert", new { page = "+A/1", macro = "../bad.emp", x = "bad", y = 2, variant = 2, numbering = "invalid", approval = true }));
        foreach (string field in new[] { "macro", "x", "variant", "numbering", "approval" })
        { Assert.Contains(result.Violations, v => v.Pointer == "/steps/0/args/" + field); }
    }

    [Fact]
    public void ArgsByteBudget_IsSharedAcrossSteps()
    {
        string args = "{\"tag\":\"-K1\",\"new_tag\":\"-K2\"" + new string(' ', 140_000) + "}";
        string json = "{\"project\":\"Demo\",\"steps\":[{\"op\":\"device.update\",\"args\":" + args + "},{\"op\":\"device.update\",\"args\":" + args + "}]}";
        Assert.Contains(PlanArguments.Check(PlanTestData.Json(json), "en_US", "IEC").Violations, v => v.Message.Contains("256 KiB", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("[]", "")]
    [InlineData("{}", "/project")]
    [InlineData("{\"project\":\"Demo\",\"steps\":{}}", "/steps")]
    [InlineData("{\"project\":\"Demo\",\"steps\":[null]}", "/steps/0")]
    [InlineData("{\"project\":\"Demo\",\"steps\":[{\"op\":\"page.create\",\"args\":[]}]}", "/steps/0/args")]
    public void MalformedEnvelope_ReturnsViolations(string json, string expectedPointer)
    { Assert.Contains(PlanArguments.Check(PlanTestData.Json(json), "en_US", "IEC").Violations, v => v.Pointer == expectedPointer); }

    [Theory]
    [InlineData("{\"tag\":3,\"new_tag\":false}", "tag")]
    [InlineData("{\"tag\":\"-K1\"}", "new_tag")]
    public void TypesAndRequiredFields_AreChecked(string json, string field)
    { Assert.Contains(Check(Step("device.update", PlanTestData.Json(json))).Violations, v => v.Pointer == "/steps/0/args/" + field); }

    [Theory]
    [InlineData("page.create")]
    [InlineData("page.update")]
    public async Task SharedValidator_BadLanguageYieldsExactlyTheToolText(string op)
    {
        CallToolResult tool = op == "page.create"
            ? await PageWriteTools.PageCreateAsync(null!, "Demo", [new() { Name = "+A/1" }], language: "bad", cancellationToken: TestContext.Current.CancellationToken)
            : await PageWriteTools.PageUpdateAsync(null!, "Demo", [new() { Name = "+A/1", Description = "text" }], language: "bad", cancellationToken: TestContext.Current.CancellationToken);
        using JsonDocument error = JsonDocument.Parse(Assert.IsType<TextContentBlock>(tool.Content[0]).Text);
        string? original = error.RootElement.GetProperty("error").GetProperty("metadata").GetProperty("violations").GetString();
        Assert.Equal(original, Assert.Single(Check(Step(op, new { pages = Pages(1), language = "bad" })).Violations).Message);
    }
}
