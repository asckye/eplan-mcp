using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Drawing-locator deletion: a read-only plan followed by one checked transaction.</summary>
public class PlacementDeleteOperation(IProjectCatalog catalog, IPageDrawingReader drawings, IPlacementRemover remover,
    ITransactionScopeFactory transactions, bool execute, Action<OpenProject, BackupTarget?> backup) : Operation<JObject, JObject>
{
    public const string Tool = "eplan_placement_delete";
    public override string Name => execute ? OperationNames.PlacementDeleteExecute : OperationNames.PlacementDeletePlan;
    public static bool Allowed(string kind) => kind is "Line" or "Arc" or "PolyLine" or "Spline" or "Rectangle" or "Text" or "PathText" or "Image" or "MacroBox"
        or "HyperLink" or "QRCode" or "DimensionItem" or "DimensionCircle" or "PlaceHolder" || IsFunction(kind);

    protected override JObject Execute(JObject args)
    {
        JObject input = execute ? args["request"] as JObject ?? throw WriteArguments.NotStarted(WriteArguments.Invalid(["Supply request."])) : args;
        OpenProject project;
        PageDrawingGeometry before;
        DrawingLocator[] items;
        Action remove;
        var targets = new List<DrawingPlacement>();
        try
        {
            if (input.Properties().Any(p => p.Name is not ("project" or "page" or "items" or "dry_run"))
                || input["project"]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)input["project"]) || input["page"]?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)input["page"])
                || input["dry_run"] is { Type: not JTokenType.Boolean } || input["items"] is not JArray { Count: >= 1 and <= 100 } list)
            {
                throw WriteArguments.Invalid(["Supply project, page and 1..100 drawing locators in items; dry_run must be boolean."]);
            }
            foreach (JToken locator in list)
            {
                if (locator is not JObject row || row.Count != 5 || row.Properties().Any(p => p.Name is "type" or "oid" ? p.Value.Type != JTokenType.Integer : p.Name is not ("project" or "page" or "digest") || p.Value.Type != JTokenType.String))
                {
                    throw WriteArguments.Invalid(["Each item must be an uncoerced drawing locator from eplan_page_get."]);
                }
                if (!int.TryParse((string?)row["type"], out int type) || type < 1 || !long.TryParse((string?)row["oid"], out long oid) || oid < 1)
                {
                    throw WriteArguments.Invalid(["Locator type and oid must be positive integers in range."]);
                }
            }
            items = list.ToObject<DrawingLocator[]>()!;
            if (items.Any(l => l.Type < 1 || l.Oid < 1 || l.Digest.Length != 64 || l.Digest.Any(c => "0123456789abcdef".IndexOf(c) < 0))
                || items.Select(l => l.Type + "/" + l.Oid).Distinct(StringComparer.Ordinal).Count() != items.Length)
            {
                throw WriteArguments.Invalid(["Items require distinct identities and SHA-256 digests."]);
            }
            project = WriteArguments.Writable(catalog, (string)input["project"]!);
            before = Read(project.Path, (string)input["page"]!);
            foreach (DrawingLocator item in items)
            {
                DrawingPlacement row = before.Placements.FirstOrDefault(p => DrawingLocators.Matches(item, p.Locator)) ?? throw Stale();
                if (row.Parent is not null)
                {
                    throw WriteArguments.Invalid(["Block members and property text cannot be deleted separately."]);
                }
                if (!Allowed(row.Kind))
                {
                    throw WriteArguments.Invalid([$"Cannot delete {row.Kind}: only independent graphical placements are allowed; devices, connections and blocks are excluded."]);
                }
                targets.Add(row);
            }
            if (execute && (string?)args["fingerprint"] != before.Fingerprint())
            {
                throw Stale();
            }
            remove = remover.Prepare(project.Path, before.Page, items);
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }
        bool hasFunctions = targets.Any(row => IsFunction(row.Kind));
        var result = new JObject { ["project"] = project.Name, ["page"] = before.Page, ["items"] = new JArray(items.Select(l => new JObject { ["type"] = l.Type, ["oid"] = l.Oid })), ["fingerprint"] = before.Fingerprint() };
        if (!execute)
        {
            return (JObject)Bridge.Core.Protocol.BridgeJson.FromObject(new WritePlan
            {
                Project = project.Name,
                ProjectPath = project.Path,
                Fingerprint = before.Fingerprint(),
                Changes = targets.Select(row =>
                {
                    bool function = IsFunction(row.Kind);
                    string name = before.Page + ":" + row.Locator.Type + "/" + row.Locator.Oid;
                    if (function && row.Properties.TryGetValue("name", out string? functionName) && !string.IsNullOrWhiteSpace(functionName))
                    {
                        name = functionName;
                    }
                    return new PlannedChange
                    {
                        Action = "delete",
                        Kind = function ? "function" : "drawing",
                        Name = function ? name + " (" + row.Kind + ")" : name,
                        OldValue = function ? "Removing the last placed function also deletes its device and part references." : null
                    };
                }).ToList(),
                Details = new() { ["preview"] = result.ToString(Formatting.None) }
            });
        }
        backup(project, args["backup"]?.ToObject<BackupTarget>());
        string unchanged = before.Fingerprint();
        PageDrawingGeometry expected = Read(project.Path, before.Page);
        expected.Placements.RemoveAll(p => items.Any(l => DrawingLocators.Matches(l, p.Locator) || p.Parent is { } parent && parent.Type == l.Type && parent.Oid == l.Oid));
        PageDrawingGeometry? expectedWithoutConnections = hasFunctions ? new PageDrawingGeometry
        {
            GridSize = expected.GridSize,
            PageSize = expected.PageSize,
            Placements = expected.Placements.Where(p => p.Kind is not ("Connection" or "DynamicConnectionLine")).ToList()
        } : null;
        Committed<bool> committed = TransactionRunner.Run(transactions, Tool, () =>
        {
            remove();
            return true;
        }, verifyCommitted: _ =>
        {
            PageDrawingGeometry actual = Read(project.Path, before.Page);
            bool targetsRemain = hasFunctions && actual.Placements.Any(p => items.Any(l => p.Locator.Type == l.Type && p.Locator.Oid == l.Oid
                || p.Parent is { } parent && parent.Type == l.Type && parent.Oid == l.Oid));
            if (hasFunctions)
            {
                actual.Placements.RemoveAll(p => p.Kind is "Connection" or "DynamicConnectionLine");
            }
            if (targetsRemain || actual.Fingerprint() != (expectedWithoutConnections ?? expected).Fingerprint())
            {
                throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Deletion changed objects outside the plan or left targets behind.");
            }
        }, verifyRollback: () =>
        {
            if (Read(project.Path, before.Page).Fingerprint() != unchanged)
            {
                throw new InvalidOperationException("Deletion rollback did not restore the page.");
            }
        });
        result["execution"] = ExecutionState.Committed;
        result["warning"] = committed.Warning;
        if (hasFunctions)
        {
            result["hint"] = "Call eplan_connection_generate for the page so connections follow the removed symbols.";
        }
        return result;
    }

    // MX_NFPA 2026-11-07: SPECIAL corners and T-nodes read as SymbolReference; removing one changes the connections like a function.
    // Codex review eval 2026-11-10: potential points and potential or connection definition points change the connections' potential,
    // colour and cross-section, so they are removed and regenerated the same way (A129).
    private static bool IsFunction(string kind) => kind is "Function" or "Terminal" or "InterruptionPoint" or "BoxedDevice" or "PLC" or "SymbolReference" or "LocationBox"
        or "PotentialDefinition" or "ConnectionDefinitionPoint";
    private PageDrawingGeometry Read(string project, string page) => drawings.Read(project, page) ?? throw new BridgeErrorException(execute ? ErrorReason.PlanStale : ErrorReason.PageNotFound, "Read the target page with eplan_page_get.");
    private static BridgeErrorException Stale() => new(ErrorReason.PlanStale, "A drawing locator or page changed; call eplan_page_get with include_drawings again.");
}

public sealed class PlacementDeleteExecuteOperation(IProjectCatalog catalog, IPageDrawingReader drawings, IPlacementRemover remover,
    ITransactionScopeFactory transactions, Action<OpenProject, BackupTarget?> backup) : PlacementDeleteOperation(catalog, drawings, remover, transactions, true, backup), IWriteOperation;
