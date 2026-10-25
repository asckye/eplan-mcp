using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Closed seven-operation adapter. One dispatcher call and outcome; each existing execute owns its transaction.</summary>
public sealed class PlanStepExecuteOperation(
    PlanStepGuardOperation guard,
    IProjectCatalog catalog,
    IPageReader pages,
    IPageWriter writer,
    IPageDescriptions descriptions,
    IDeviceReader devices,
    IDeviceRenamer renamer,
    IPageRemover remover,
    IPageRenumberer pageRenumberer,
    IDeviceRenumberer deviceRenumberer,
    IMacroReader macros,
    IMacroInserter inserter,
    IProjectBackup backup,
    ITransactionScopeFactory transactions) : Operation<PlanStepExecuteArgs, object?>, IWriteOperation
{
    public override string Name => OperationNames.PlanStepExecute;

    public override object ParseArgs(JToken? args)
    {
        try
        {
            return args is JObject ? args.ToObject<PlanStepExecuteArgs>(JsonSerializer.Create(new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver(),
                MissingMemberHandling = MissingMemberHandling.Error,
            }))! : throw InvalidArgument("Supply a typed plan step object.");
        }
        catch (JsonException)
        {
            throw WriteArguments.NotStarted(InvalidArgument("Unknown or invalid step fields; approval answers and operation names are not accepted."));
        }
        catch (BridgeErrorException error)
        {
            throw WriteArguments.NotStarted(error);
        }
    }

    protected override object? Execute(PlanStepExecuteArgs args)
    {
        FileStream? file = null;
        bool executing = false;
        try
        {
            object[] choices = [args.PageCreate!, args.PageUpdate!, args.PageDelete!, args.PageRenumber!,
                args.DeviceUpdate!, args.DeviceRenumber!, args.MacroInsert!];
            if (!Regex.IsMatch(args.PlanId ?? "", @"\A[0-9a-f]{32}\z") || args.Index < 1 || args.Index > args.Total
                || args.Total > 20 || choices.Count(c => c is not null) != 1 || args.Guard is null
                || args.ApprovedChanges is null || args.ApprovedChanges.Count is < 1 or > 50 || string.IsNullOrEmpty(args.Fingerprint)
                || args.DeviceRenumber is { Pages: null or { Count: < 1 or > 50 } })
            {
                throw InvalidArgument("Supply a valid plan ID, index/total, guard, effective changes, fingerprint and exactly one supported typed argument.");
            }

            object selected = choices.Single(c => c is not null);
            JObject input = (JObject)BridgeJson.FromObject(selected);
            if (!string.Equals((string?)input["project"], args.Guard.Project, StringComparison.OrdinalIgnoreCase)
                || input["fingerprint"]!.Type != JTokenType.Null || input["backup"] is { Type: not JTokenType.Null })
            {
                throw InvalidArgument("The typed project must equal the guard project; supply fingerprint and backup only on the step envelope.");
            }

            guard.Execute(args.Guard);
            if (args.MacroInsert is { } macro)
            {
                string root = macros.MacroRoot() is { } resolved && Directory.Exists(resolved)
                    ? resolved : throw InvalidArgument("EPLAN's macro directory is unavailable; check its settings before writing.");
                file = new FileStream(MacroFiles.Resolve(root, macro.Macro), FileMode.Open, FileAccess.Read, FileShare.Read);
                CheckMacro(macro, args.MacroConstraint, file);
            }

            string op = selected switch
            {
                PageCreateArgs => "page.create",
                PageUpdateArgs => "page.update",
                PageDeleteArgs => "page.delete",
                PageRenumberArgs => "page.renumber",
                DeviceUpdateArgs => "device.update",
                DeviceRenumberArgs => "device.renumber",
                _ => "macro.insert",
            };
            var named = new PlanUndoScopeFactory(transactions, args, op);
            (IOperation Plan, IOperation Execute) operations = selected switch
            {
                PageCreateArgs => (new PageCreatePlanOperation(catalog, pages, writer),
                    new PageCreateExecuteOperation(catalog, pages, writer, named)),
                PageUpdateArgs => (new PageUpdatePlanOperation(catalog, pages, descriptions),
                    new PageUpdateExecuteOperation(catalog, pages, descriptions, named)),
                PageDeleteArgs => (new PageDeletePlanOperation(catalog, remover),
                    new PageDeleteExecuteOperation(catalog, pages, remover, backup, named)),
                PageRenumberArgs => (new PageRenumberPlanOperation(catalog, pages, pageRenumberer),
                    new PageRenumberExecuteOperation(catalog, pages, pageRenumberer, backup, named)),
                DeviceUpdateArgs => (new DeviceUpdatePlanOperation(catalog, devices, renamer),
                    new DeviceUpdateExecuteOperation(catalog, devices, renamer, named)),
                DeviceRenumberArgs => (new DeviceRenumberPlanOperation(catalog, deviceRenumberer),
                    new DeviceRenumberExecuteOperation(catalog, pages, deviceRenumberer, backup, named)),
                _ => (new MacroInsertPlanOperation(catalog, pages, devices, macros, inserter),
                    new MacroInsertExecuteOperation(catalog, pages, devices, macros, inserter, named)),
            };
            var actual = (WritePlan)operations.Plan.Execute(selected)!;
            if (!string.Equals(actual.Fingerprint, args.Fingerprint, StringComparison.Ordinal)
                || !Equal(actual.Changes, args.ApprovedChanges)
                || (args.MacroInsert is null || args.ApprovedDetails is not null) && !Equal(actual.Details, args.ApprovedDetails))
            {
                throw Stale();
            }

            OperationContext.Current.ThrowIfCancellationRequested();
            input["fingerprint"] = args.Fingerprint;
            if (selected is PageDeleteArgs or PageRenumberArgs or DeviceRenumberArgs)
            {
                input["backup"] = BridgeJson.FromObject(args.Backup);
            }

            object executionArgs = operations.Execute.ParseArgs(input);
            executing = true;
            return operations.Execute.Execute(executionArgs);
        }
        catch (BridgeErrorException error) when (!executing)
        {
            throw WriteArguments.NotStarted(error);
        }
        catch (Exception error) when (!executing)
        {
            throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.Internal,
                "The step could not be guarded or planned; nothing was changed. Recompile the plan before writing.", error));
        }
        finally
        {
            file?.Dispose();
        }
    }

    private void CheckMacro(MacroInsertArgs args, PlanMacroConstraint? constraint, FileStream file)
    {
        if (constraint is null || args.Variant != 0 || args.Numbering != "keep")
        {
            throw InvalidArgument("Plan insertion requires a complete compiled macro constraint, variant 0 and numbering keep.");
        }

        PlanMacroInspectData expected = constraint.Description;
        using var sha = SHA256.Create();
        string hash = BitConverter.ToString(sha.ComputeHash(file)).Replace("-", string.Empty).ToLowerInvariant();
        if (!Equal(args, constraint.Arguments) || !string.Equals(hash, expected.Sha256, StringComparison.Ordinal))
        {
            throw Stale();
        }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        MacroTargetPage page = inserter.ReadPage(project.Path, args.Page) ?? throw Stale();
        if (page.Type != "Circuit" || page.Objects.Count != 0
            || !Equal(PlanMacroInspectOperation.Describe(macros.ReadMacro(project.Path, file.Name, defaultMultiLine: true), hash), expected))
        {
            throw Stale();
        }
    }

    private static bool Equal(object? left, object? right) =>
        JToken.DeepEquals(PlanGuardScopeRules.Canonical(left!), PlanGuardScopeRules.Canonical(right!));

    private static BridgeErrorException Stale() => new(ErrorReason.PlanStale,
        "The step no longer matches its approved changes, constraints or temporary fingerprint; recompile before writing.", retryable: false);
}
