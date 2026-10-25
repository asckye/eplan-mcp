using System.Text;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Plans;
using EplanMcp.Host.Tests.Plans;
using EplanMcp.Host.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace EplanMcp.Host.Tests.Tools;

public sealed partial class PlanApplyProtocolTests
{
    [Theory]
    [InlineData("page.create", 0)]
    [InlineData("device.update", 2)]
    [InlineData("macro.insert", 1)]
    public async Task Version2_CompileGetDryRunApply_ThreeWritesNeverRequestWholeStandard(string op, int readPages)
    {
        ConfigureLocalWrite(op);
        _additionalNames = [.. Enumerable.Range(1, 10_001).Select(i => new PlanOccupiedName("function", "+REMOTE-K" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)))];
        await WithClient(async (client, services) =>
        {
            var request = new Dictionary<string, object?>
            {
                ["project"] = "Line1",
                ["steps"] = new object[] { new { op, args = PlanStepCompilationData.Input(op).Args } },
            };
            JsonElement compiled = ContextToolTests.Data(await Call(client, WorkflowTools.PlanTool, request));
            string id = compiled.GetProperty("planId").GetString()!;
            PlanStore store = services.GetRequiredService<PlanStore>();
            PlanRecord record = (await store.ReadAsync(id, Token))!;
            Assert.Equal("2", record.Definition.CompilerVersion);
            Assert.Equal(readPages, record.Definition.Steps[0].ReadScope.Pages.Length);
            string bytes = Encoding.UTF8.GetString(await store.ReadEvidenceAsync(id, record.Definition.ContextRef![7..], Token));
            PlanBaseline baseline = PlanJson.Read<PlanBaseline>(bytes);
            Assert.Equal(readPages, baseline.Snapshot.Pages.Length);
            Assert.Null(baseline.Snapshot.SnapshotHash);
            Assert.True(baseline.Names.Length > 10_000);
            Assert.Contains(baseline.Names, n => n.Name == "+A10/1");

            JsonElement inspected = ContextToolTests.Data(await Call(client, PlanTools.GetTool,
                new()
                {
                    ["plan_id"] = id,
                    ["response_format"] = "detailed",
                    ["limit"] = 200
                }));
            Assert.Equal("2", inspected.GetProperty("plan").GetProperty("compilerVersion").GetString());
            Assert.Equal(record.PlanHash, inspected.GetProperty("plan").GetProperty("planHash").GetString());
            Assert.Contains(inspected.GetProperty("entries").EnumerateArray(), e => e.GetProperty("key").GetString() == "readScope");
            string original = await File.ReadAllTextAsync(Path.Combine(_locations.State, "plans", id + ".json"), Token);
            JsonElement dry = ContextToolTests.Data(await Call(client, PlanApplyTools.ApplyTool, new()
            {
                ["plan_id"] = id,
                ["dry_run"] = true
            }));
            Assert.Equal("not_started", dry.GetProperty("execution").GetString());
            Assert.Equal(original, await File.ReadAllTextAsync(Path.Combine(_locations.State, "plans", id + ".json"), Token));
            Assert.Empty(_fake.Writes);

            var applyCall = await Call(client, PlanApplyTools.ApplyTool, new()
            {
                ["plan_id"] = id
            });
            Assert.Equal(op == "macro.insert", applyCall.IsError == true);
            JsonElement applied = applyCall.StructuredContent!.Value;
            Assert.Equal("committed", applied.GetProperty("execution").GetString());
            Assert.Equal(op == "macro.insert" ? "skipped" : "passed", applied.GetProperty("verification").GetString());
            Assert.True(File.Exists(applied.GetProperty("backup").GetString()));
            PlanRecord terminal = (await store.ReadAsync(id, Token))!;
            Assert.Equal(record.PlanHash, terminal.PlanHash);
            var coverage = await PlanCoverage.ReadAsync(store, terminal, Token);
            Assert.Contains(coverage, c => c.UnobservedPages.Contains("+A10/1", StringComparer.Ordinal));
            Assert.Single(_fake.Writes);
            Assert.DoesNotContain(_fake.Calls, c => c.Operation == OperationNames.DeviceList);
            Assert.All(_fake.Calls.Where(c => c.Operation == OperationNames.ProjectSnapshot), c =>
            {
                var args = Assert.IsType<ProjectSnapshotArgs>(c.Args);
                Assert.NotNull(args.Pages);
                Assert.NotEmpty(args.Pages);
                Assert.DoesNotContain("+A10/1", args.Pages);
                Assert.Equal(SnapshotProfiles.StandardName, args.Profile);
            });
            PlanStepExecuteArgs sent = Assert.IsType<PlanStepExecuteArgs>(Assert.Single(_fake.Calls, c => c.Operation == OperationNames.PlanStepExecute).Args);
            Assert.Equal(readPages, sent.Guard.ReadScope!.Pages.Count);
        });
    }

    [Theory]
    [InlineData("page.update", 1)]
    [InlineData("page.delete", 2)]
    [InlineData("page.renumber", 3)]
    [InlineData("device.renumber", 3)]
    public async Task Version2_OtherCatalogSteps_CompileWithOriginalClosureAndRenumberScopes(string op, int pages)
    {
        ConfigureLocalWrite(op);
        _fake.Status.Handshake!.Operations = [.. _fake.Status.Handshake.Operations!, op + ".plan", op + ".execute"];
        if (op != "page.update")
        {
            _fake.Handlers[op + ".plan"] = _ =>
            {
                WritePlan plan = PlanStepCompilationData.Plan(op);
                plan.ProjectPath = _current.Project.Path;
                return plan;
            };
        }
        await WithClient(async (client, services) =>
        {
            JsonElement result = ContextToolTests.Data(await Call(client, WorkflowTools.PlanTool, new()
            {
                ["project"] = "Line1",
                ["steps"] = new object[] { new { op, args = PlanStepCompilationData.Input(op).Args } },
            }));
            PlanRecord record = (await services.GetRequiredService<PlanStore>().ReadAsync(result.GetProperty("planId").GetString()!, Token))!;
            Assert.Equal("2", record.Definition.CompilerVersion);
            Assert.Equal(pages, record.Definition.Steps[0].ReadScope.Pages.Length);
            Assert.DoesNotContain(_fake.Calls, c => c.Args is ProjectSnapshotArgs { Pages: null });
            if (op == "page.delete")
            {
                Assert.Contains(record.Definition.Steps[0].Expected, e => e.GetProperty("tag").GetString() == "orphaned");
            }
        });
    }

    [Fact]
    public async Task Version2_PageLessRename_RefusesAtCompilationBeforeContentReadOrWrite()
    {
        ConfigureLocalWrite("device.update");
        Func<object?, object> handler = _fake.Handlers[OperationNames.PlanFunctions];
        _fake.Handlers[OperationNames.PlanFunctions] = args =>
        {
            var data = (PlanFunctionsData)handler(args);
            data.Functions.Add(new()
            {
                Type = 17,
                Oid = 999,
                Name = "+A1-K1",
            });
            return data;
        };
        await WithClient(async (client, _) =>
        {
            JsonElement error = ContextToolTests.Error(await Call(client, WorkflowTools.PlanTool, new()
            {
                ["project"] = "Line1",
                ["steps"] = new object[] { new { op = "device.update", args = PlanStepCompilationData.Input("device.update").Args } },
            }));
            Assert.Equal(ErrorReason.CapabilityMissing, error.GetProperty("reason").GetString());
            Assert.Contains("object.resolve:functions_without_page", error.GetRawText(), StringComparison.Ordinal);
            Assert.DoesNotContain(_fake.Calls, c => c.Operation == OperationNames.ProjectSnapshot);
            Assert.Empty(_fake.Writes);
        });
    }

    [Fact]
    public async Task StoredVersion1_GetDryRunApply_UsesOriginalWholeProjectPathWithoutRehashing()
    {
        await WithClient(async (client, services) =>
        {
            // Establish this MCP session's owner, then publish an independently compiled legacy record.
            await Compile(client);
            PlanStore store = services.GetRequiredService<PlanStore>();
            PlanBaseline baseline = PlanScopeTestData.Baseline([], ["+A1/1"], _current.Project.Path);
            baseline = baseline with
            {
                Snapshot = _current
            };
            PlanStep create = await new PlanStepCompiler(_fake).CompileAsync(baseline, PlanStepCompilationData.Input("page.create"), Token);
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(baseline, PlanJson.Options);
            string hash = PlanStore.Hash(bytes);
            PlanDefinition definition = PlanTestData.Definition() with
            {
                PlanId = Guid.NewGuid().ToString("N"),
                CompilerVersion = "1",
                Owner = services.GetRequiredService<TaskRegistry>().OwnerFor(_server!),
                CreatedAt = _clock.GetUtcNow(),
                ExpiresAt = _clock.GetUtcNow().AddHours(24),
                Project = _current.Project,
                EplanVersion = "2026",
                EplanVariant = "Pro Panel",
                Steps = [create],
                Resources = [],
                Sources = [],
                RequiredOperations = [.. _fake.Status.Handshake!.Operations!],
                ContextRef = "sha256:" + hash,
                Digests = new(_current.ComposedHash, _current.MembersHash, baseline.NamesHash, null),
            };
            string changes = PlanDiffPreview.For(definition.Steps);
            definition = definition with
            {
                Digests = definition.Digests with
                {
                    ChangesDigest = PlanStore.Hash(Encoding.UTF8.GetBytes(changes))
                }
            };
            PlanRecord stored = await store.PublishAsync(definition, changes, new Dictionary<string, byte[]> { [hash] = bytes }, Token);
            services.GetRequiredService<PlanTrust>().Register(definition);
            _fake.Calls.Clear();

            JsonElement queried = ContextToolTests.Data(await Call(client, PlanTools.GetTool, new()
            {
                ["plan_id"] = definition.PlanId
            }));
            Assert.Equal("1", queried.GetProperty("plan").GetProperty("compilerVersion").GetString());
            Assert.Empty(_fake.Calls);
            await Call(client, PlanApplyTools.ApplyTool, new()
            {
                ["plan_id"] = definition.PlanId,
                ["dry_run"] = true
            });
            JsonElement result = ContextToolTests.Data(await Call(client, PlanApplyTools.ApplyTool, new()
            {
                ["plan_id"] = definition.PlanId
            }));
            Assert.Equal("committed", result.GetProperty("execution").GetString());
            Assert.Equal("passed", result.GetProperty("verification").GetString());
            Assert.Contains(_fake.Calls, c => c.Args is ProjectSnapshotArgs { Pages: null });
            Assert.Contains(_fake.Calls, c => c.Operation == OperationNames.DeviceList);
            Assert.DoesNotContain(_fake.Calls, c => c.Operation == OperationNames.PlanNames);
            PlanRecord after = (await store.ReadAsync(definition.PlanId, Token))!;
            Assert.Equal(stored.PlanHash, after.PlanHash);
            Assert.Equal(PlanCanonical.Serialize(definition), PlanCanonical.Serialize(after.Definition));
            Assert.Equal(bytes, await store.ReadEvidenceAsync(definition.PlanId, hash, Token));
        });
    }

    private void ConfigureLocalWrite(string op)
    {
        _current = PlanVerificationTestData.Copy(PlanScopeTestData.Baseline(op == "macro.insert" ? [] : null,
            project: _current.Project.Path).Snapshot, _ => { });
        _fake.Status.Handshake!.Operations = [.. _fake.Status.Handshake.Operations!, OperationNames.DeviceUpdatePlan, OperationNames.DeviceUpdateExecute,
            OperationNames.MacroInsertPlan, OperationNames.MacroInsertExecute, OperationNames.PlanMacroInspect];
        _fake.Handlers[OperationNames.PlanFunctions] = a => new PlanFunctionsData
        {
            Project = _current.Project.Name,
            ProjectPath = _current.Project.Path,
            Complete = true,
            Limit = 10_000,
            Functions = [.. _current.Pages.SelectMany(p => p.Objects).Where(o => o.Kind == "function" && ((PlanFunctionsArgs)a!).Names.Contains(o.Name!, StringComparer.Ordinal))
                .Select(o => new PlanFunction { Kind = o.Kind, Type = o.Type, Oid = o.Oid, Name = o.Name!, Page = o.Page, IsMain = o.Main!.Value })],
        };
        foreach (string operation in new[] { "device.update", "macro.insert" })
        {
            _fake.Handlers[operation + ".plan"] = _ =>
            {
                WritePlan plan = PlanStepCompilationData.Plan(operation);
                plan.ProjectPath = _current.Project.Path;
                return plan;
            };
        }
        _fake.Handlers[OperationNames.PlanMacroInspect] = _ => new PlanMacroInspectData
        {
            Sha256 = new string('a', 64),
            ObjectCount = 1,
            Functions = [new() { Kind = "function", Category = "Coil", Main = true, Name = "+-K8", StructureSegments = ["+"] }],
        };
        _fake.Handlers[OperationNames.PlanStepExecute] = _ =>
        {
            _current = PlanVerificationTestData.After(op, _current);
            return JsonSerializer.SerializeToElement(new
            {
            });
        };
    }
}
