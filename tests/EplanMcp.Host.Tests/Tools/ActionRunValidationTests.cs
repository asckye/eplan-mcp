using System.Text.Json;
using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

[Trait("Size", "Small")]
public sealed class ActionRunValidationTests
{
    public static TheoryData<string> Entries
    {
        get
        {
            var cases = new TheoryData<string>();
            foreach (string entry in ActionRunCatalog.Entries.Keys) { cases.Add(entry); }
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(Entries))]
    public void EveryEntry_RequiresEachFieldOnce_AndKeepsTextAndTypeRules(string entry)
    {
        JsonObject example = ActionRunUsage.Example(ActionRunCatalog.Entries[entry]);
        string? project = example["project"]?.GetValue<string>();
        var fields = example["arguments"]!.AsObject();
        foreach ((string key, JsonNode? original) in fields.ToList())
        {
            foreach (JsonNode? invalid in new JsonNode?[] { null, JsonValue.Create(1), JsonValue.Create(false), new JsonArray(), new JsonArray(original!.DeepClone()), new JsonArray(original.DeepClone(), original.DeepClone()) })
            {
                var changed = fields.DeepClone().AsObject();
                changed[key] = invalid;
                Invalid(entry, project, changed, key);
            }
            foreach (string invalid in new[] { "", " ", "/flag", "a'b", "a\"b", "a\nb", "a\u007fb", "a\u0085b", new string('a', 101) })
            {
                var changed = fields.DeepClone().AsObject();
                changed[key] = invalid;
                Invalid(entry, project, changed, key);
            }
            var missing = fields.DeepClone().AsObject();
            missing.Remove(key);
            Invalid(entry, project, missing, key);
            string duplicate = fields.ToJsonString()[..^1] + "," + JsonSerializer.Serialize(key) + ":" + original!.ToJsonString() + "}";
            Invalid(entry, project, fields, key, JsonDocument.Parse(duplicate).RootElement);
        }
        ActionRunArgs accepted = new ActionRunCatalog().Check(entry, project, JsonSerializer.SerializeToElement(fields)).Args;
        Assert.Equal(fields.Count, accepted.Parameters.Count);
    }

    [Theory]
    [MemberData(nameof(Entries))]
    public void EveryEntry_RejectsFixedUnknownAndExpandedNames_AndEnforcesProjectScope(string entry)
    {
        JsonObject rule = ActionRunCatalog.Entries[entry];
        JsonObject example = ActionRunUsage.Example(rule);
        string? project = example["project"]?.GetValue<string>();
        foreach (string key in rule["fixed"]!.AsObject().Select(p => p.Key).Concat(["PROJECTNAME", "Project", "DESTINATIONPATH", "TYPE", "LAYER1", "VISIBLE1", "PROPERTYID1", "PROPERTYVALUE1", "LAYER2", "VISIBLE2", "PROPERTYID2", "PROPERTYVALUE2", "unknown"]).Distinct(StringComparer.Ordinal))
        {
            var fields = example["arguments"]!.DeepClone().AsObject();
            fields[key] = "untrusted";
            Invalid(entry, project, fields, key);
        }
        var arguments = example["arguments"]!;
        Invalid(entry, project is null ? "Line1" : null, arguments, "project");
        Invalid(entry, " ", arguments, "project");
    }

    [Theory]
    [MemberData(nameof(Entries))]
    public void EveryEntry_ReportsAllViolationsAllowedNamesAndExactUsageHint(string entry)
    {
        JsonObject rule = ActionRunCatalog.Entries[entry];
        string[] required = [.. rule["parameters"]!.AsObject().Where(p => !p.Key.EndsWith('?')).Select(p => p.Key.EndsWith('1') ? p.Key[..^1] : p.Key)];
        BridgeError error = Invalid(entry, ActionRunCatalog.Text(rule, "scope") == "project" ? null : "Line1", new JsonObject { ["unexpected"] = "x" }, "unexpected");
        Assert.Equal(required.Length + 2, error.Metadata!["violations"].Split('\n').Length);
        foreach (string key in required) { Assert.Contains(key, error.Metadata["violations"], StringComparison.Ordinal); }
        string[] allowed = [.. rule["parameters"]!.AsObject().Select(p => p.Key.EndsWith('1') ? p.Key[..^1] : p.Key.TrimEnd('?'))];
        Assert.Equal(string.Join(", ", allowed), error.Metadata["allowedArguments"]);
        Assert.Contains("topic: \"" + entry + "\"", error.Metadata["hint"], StringComparison.Ordinal);
        Assert.Contains("eplan_tool_usage", error.Message, StringComparison.Ordinal);
        Invalid(entry, ActionRunCatalog.Text(rule, "scope") == "project" ? "Line1" : null, new JsonArray(), "arguments");
    }

    [Theory]
    [MemberData(nameof(Entries))]
    public void EveryExport_KeepsFilenameRulesAndExtensionLengthLimits(string entry)
    {
        JsonObject rule = ActionRunCatalog.Entries[entry];
        string? key = rule["parameters"]!.AsObject().FirstOrDefault(p => p.Value!.GetValue<string>() == "file").Key;
        if (key is null) { return; }
        JsonObject example = ActionRunUsage.Example(rule);
        var fields = example["arguments"]!.AsObject();
        string? project = example["project"]?.GetValue<string>();
        string extension = ActionRunCatalog.Text(rule, "extension").Split('|')[0];
        foreach (string invalid in new[] { @"D:\escape", @"\\server\share", "../escape", "..", ".", "CON", "NUL.xml", "COM¹", "LPT²", "trailing.", "trailing ", "a<b", "a>b", "a:b", "a|b", "a?b", "a*b", "file.wrong", extension, new string('a', 101 - extension.Length) })
        {
            fields[key] = invalid;
            Invalid(entry, project, fields, key);
        }
        foreach (string valid in new[] { "层 表", new string('a', 100 - extension.Length), "layers" + extension.ToUpperInvariant() })
        {
            fields[key] = valid;
            ActionRunArgs args = new ActionRunCatalog().Check(entry, project, JsonSerializer.SerializeToElement(fields)).Args;
            string actual = args.Parameters.Single(p => p.Name == key).Value;
            Assert.Equal(valid.EndsWith(extension, StringComparison.OrdinalIgnoreCase) ? valid : valid + extension, actual);
        }
    }

    [Theory]
    [InlineData("PlcDcXMLExchangerUniversal", null, "bus.pbf", true)]
    [InlineData("PlcDcXMLExchangerUniversal", "AutomationML AR APC V1.4.0", "bus.pbf", false)]
    [InlineData("PlcDcXMLExchangerUniversal", null, "bus.aml", false)]
    [InlineData("PlcDcAMLExchangerGeneral", null, "bus.aml", true)]
    [InlineData("PlcDcAMLExchangerGeneral", "AutomationML AR APC V1.4.0", "bus.aml", true)]
    [InlineData("PlcDcAMLExchangerGeneral", "unverified", "bus.aml", false)]
    [InlineData("PlcDcAMLExchangerGeneral", null, "bus.pbf", false)]
    [InlineData("unverified", null, "bus.pbf", false)]
    public void Plc_OnlyAllowsTheThreeVerifiedCombinations(string converter, string? format, string file, bool valid)
    {
        var fields = new JsonObject { ["CONFIGURATIONPROJECT"] = "Phoenix1", ["CONVERTERID"] = converter, ["DESTINATIONFILE"] = file };
        if (format is not null) { fields["FORMAT"] = format; }
        if (valid) { new ActionRunCatalog().Check("plcservice/BUSDATAEXPORT", "Line1", JsonSerializer.SerializeToElement(fields)); }
        else { Invalid("plcservice/BUSDATAEXPORT", "Line1", fields); }
    }

    [Theory]
    [InlineData("EXPORTPARTS", "22001")]
    [InlineData("EXPORTMANUFACTURERS", "22900")]
    [InlineData("EXPORTCONSTRUCTIONS", "22931")]
    [InlineData("EXPORTCONNECTIONPATTERNS", "22945")]
    [InlineData("EXPORTACCESSORYLISTS", "22960")]
    [InlineData("EXPORTACCESSORYPLACEMENTS", "22970")]
    public void Parts_EachEntryPinsItsPropertyIdAndRequiresOneExactRecord(string type, string id)
    {
        string entry = "partsmanagementapi/" + type;
        var fields = ActionRunUsage.Example(ActionRunCatalog.Entries[entry])["arguments"]!.AsObject();
        Assert.Equal(id, fields["PROPERTYID"]!.GetValue<string>());
        foreach (string invalid in new[] { "22000", "022001", "22900.0", "unverified" })
        { fields["PROPERTYID"] = invalid; Invalid(entry, null, fields, "PROPERTYID"); }
        fields["PROPERTYID"] = id;
        foreach (string invalid in new[] { "*", "A?B" })
        { fields["PROPERTYVALUE"] = invalid; Invalid(entry, null, fields, "PROPERTYVALUE"); }
    }

    [Theory]
    [InlineData("ARTICLES", "Library_01-Backup", true)]
    [InlineData("LANGUAGES", "Library_01-Backup", true)]
    [InlineData("articles", "Library", false)]
    [InlineData("unverified", "Library", false)]
    [InlineData("ARTICLES", "Library.zw6", false)]
    [InlineData("ARTICLES", "Library name", false)]
    [InlineData("ARTICLES", "库", false)]
    public void Backup_PinsTheDatabaseTypesAndAsciiArchiveStem(string type, string stem, bool valid)
    {
        var fields = new JsonObject { ["MDTYPE"] = type, ["ARCHIVENAME"] = stem };
        if (valid) { new ActionRunCatalog().Check("backup/MASTERDATA", null, JsonSerializer.SerializeToElement(fields)); }
        else { Invalid("backup/MASTERDATA", null, fields); }
    }

    private static BridgeError Invalid(string entry, string? project, JsonNode fields, string? violation = null, JsonElement? raw = null)
    {
        BridgeError error = Assert.Throws<BridgeCallException>(() => new ActionRunCatalog().Check(entry, project, raw ?? JsonSerializer.SerializeToElement(fields))).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
        if (violation is not null) { Assert.Contains(violation, error.Metadata!["violations"], StringComparison.Ordinal); }
        Assert.Contains(entry, error.Metadata!["hint"], StringComparison.Ordinal);
        Assert.NotEmpty(error.Metadata["allowedArguments"]);
        return error;
    }
}
