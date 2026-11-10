using System.Text.Json;
using System.Text.Json.Nodes;

namespace EplanMcp.Large.Tests;

/// <summary>Runs the master data tests with this variant's databases pointed at copies below scratch (README.md, api-notes A103). Stops before any write unless EPLAN resolves both paths to the copies.</summary>
[Trait("Size", "Large")]
public sealed class MasterDataOnCopyTests
{
    private static readonly string[] _tests = ["PartBuildTests", "PartImportTests", "PartSyncTests", "TranslationImportTests"];

    /// <summary>The parts database the copy was made from; <c>PartBuildTests</c> compares the fresh copy with it and checks it stays unchanged.</summary>
    public const string PartsSource = "EPLANMCP_LARGE_PARTS_SOURCE";

    public static bool Enabled => Environment.GetEnvironmentVariable("EPLANMCP_MASTERDATA_COPY") is { Length: > 0 };

    [Fact(SkipUnless = nameof(Enabled), Skip = "Set EPLANMCP_MASTERDATA_COPY to the copy folder.")]
    public async Task MasterDataTests_RunOnlyOnCopies_RecordEvidence()
    {
        string root = Environment.GetEnvironmentVariable("EPLANMCP_MASTERDATA_COPY")!;
        string parts = Path.Combine(root, "parts", "EPLAN_parts.alk");
        string translate = Path.Combine(root, "translate", "Translate.db");
        string helper = OfflineHost.RepositoryFile("tests", "EplanMcp.Large.ConnectionHelper", "EplanMcp.Large.ConnectionHelper.dll");
        await PlanStepLiveSession.ActionAsync("EplApiModuleAction /register:\"" + helper + "\" /Quiet");
        var evidence = new JsonObject();
        void Save() => File.WriteAllText(Path.Combine(root, "evidence.json"), evidence.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        async Task<JsonNode> Native(object request)
        {
            string file = Path.Combine(root, "mdcopy-" + Guid.NewGuid().ToString("N") + ".json");
            File.WriteAllText(file, JsonSerializer.Serialize(request));
            await PlanStepLiveSession.ActionAsync("EplanMcpLargeMasterDataCopy /REQUEST:\"" + file + "\"");
            return JsonNode.Parse(File.ReadAllText(file + ".result.json"))!;
        }
        async Task Guard(string step)
        {
            JsonNode state = await Native(new { op = "read" });
            evidence["guard " + step] = state.DeepClone();
            Save();
            Assert.Equal(parts, (string?)state["partsResolved"], ignoreCase: true);
            Assert.Equal(translate, (string?)state["translateResolved"], ignoreCase: true);
        }
        async Task Run(string name, Func<Task> test)
        {
            await Guard(name);
            Environment.SetEnvironmentVariable(PartImportTests.OptIn, "1");
            try
            {
                await test();
                evidence[name] = "passed";
            }
            catch (Exception error) { evidence[name] = "FAILED " + error.GetType().Name + " " + error.Message + " | " + error.StackTrace; }
            finally { Environment.SetEnvironmentVariable(PartImportTests.OptIn, null); }
            Save();
        }
        try
        {
            evidence["set"] = (await Native(new { op = "set", parts, translate })).DeepClone();
            Save();
            // First, while the parts copy is still byte-identical to its source.
            Assert.False(string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(PartsSource)), "Set " + PartsSource + " to the .alk the copy was made from.");
            Environment.SetEnvironmentVariable("EPLANMCP_LARGE_PARTS_COPY", parts);
            try
            { await Run("PartBuildTests", () => new PartBuildTests().ThreeFictionalParts_AreBuiltOnlyOnACopy_ThenExportedAndCompared()); }
            finally { Environment.SetEnvironmentVariable("EPLANMCP_LARGE_PARTS_COPY", null); }
            await Run("PartImportTests", () => new PartImportTests().ATestPart_IsAddedOnceAfterABackup_ThenLeftAsItIs());
            await Run("PartSyncTests", () => new PartSyncTests().TheCopysParts_AreSynchronizedAfterTwoBackups_ThenThereIsNothingToDo());
            await Run("TranslationImportTests", () => new TranslationImportTests().TestEntry_IsAddedOnceAndReimportAddsNothing_WithApprovalAndBackupEveryTime());
            await Guard("end");
        }
        finally
        { await PlanStepLiveSession.ActionAsync("EplApiModuleAction /unregister:\"EplanMcp.Large.ConnectionHelper\" /Quiet"); }
        Assert.All(_tests, name => Assert.Equal("passed", (string?)evidence[name]));
    }
}
