using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>O3/A52: two commits in one call; a failed generation is rolled back before undoing committed placement.</summary>
public sealed class ConnectionCreateExecuteOperation(IConnectionCreator creator, IConnectionGenerator generator, ITransactionScopeFactory transactions)
    : Operation<JObject, JObject>, IWriteOperation
{
    public override string Name => OperationNames.ConnectionCreateExecute;
    protected override JObject Execute(JObject args)
    {
        JObject input = args["request"] as JObject ?? throw WriteArguments.NotStarted(WriteArguments.Invalid(["/request: supply the planned request."]));
        string fingerprint = (string?)args["fingerprint"] ?? throw WriteArguments.NotStarted(WriteArguments.Invalid(["/fingerprint: supply the plan fingerprint."]));
        ConnectionCreatePlanner.Scene scene;
        JObject plan;
        Action place;
        try
        {
            scene = creator.Read((string?)input["project"] ?? "", (string?)input["page"] ?? "");
            plan = ConnectionCreatePlanner.Plan(input.ToString(Formatting.None), scene, creator, fingerprint);
            if (!plan["symbols"]!.Any() || !plan["logicalAfterGenerate"]!.Any() || plan["interruptionScope"] is not null && !plan["graphicalAutoconnect"]!.Any())
            { throw new BridgeErrorException(ErrorReason.CapabilityMissing, "Supply symbols with connected logical endpoints; empty generation cannot provide 2 undo steps."); }
            place = creator.Prepare(scene.Project.Path, scene.Page.Page, (JArray)plan["symbols"]!);
        }
        catch (BridgeErrorException ex) { throw WriteArguments.NotStarted(ex); }
        string project = scene.Project.Path, page = scene.Page.Page;
        bool interruptions = plan["interruptionScope"] is not null, generationCommitted = false;
        string[] pages = interruptions ? InterruptionPointPlanning.Pages(plan) : [page];
        Committed<bool> placed = TransactionRunner.Run(interruptions ? creator.PlacementTransactions(project) : transactions, "eplan_connection_create", () => { place(); return true; }, _ => true);
        if (placed.Warning is not null)
        { throw Unknown(); }
        try
        {
            string Segments(IEnumerable<double[][]> values) => string.Join(";", values.Select(s => string.Join("|", s.Select(p => new JArray(p).ToString(Formatting.None)).OrderBy(p => p, StringComparer.Ordinal))).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal));
            if (Segments(creator.Read(project, page).Graphical) != Segments(scene.Graphical.Concat(plan["graphicalAutoconnect"]!.ToObject<List<double[][]>>()!)))
            { throw Mismatch(); }
            Committed<List<string?[]>> generated = TransactionRunner.Run(transactions, "eplan_connection_create", () =>
            {
                generator.Generate(project, pages);
                List<string?[]> actual = pages.SelectMany(p => creator.Endpoints(project, p)).ToList();
                if (interruptions && plan["interruptionScope"]!["preservedEndpoints"]!.Any(p => !actual.Any(a => a[0] == (string?)p[0] && a[1] == (string?)p[1])))
                { throw Mismatch(); }
                var added = actual.Select(p => p.ToArray()).ToList();
                foreach (ConnectionRecord before in interruptions ? [] : scene.Logical)
                {
                    int index = added.FindIndex(p => p[0] == before.StartDevice && p[1] == before.EndDevice);
                    if (index < 0)
                    { throw Mismatch(); }
                    added.RemoveAt(index);
                }
                List<HashSet<string>> groups = plan["logicalAfterGenerate"]!.Select(g => new HashSet<string>(g["pins"]!.Values<string>().Select(p => p!).Concat((bool)g["potentialStartIsNull"]! ? ["<potential>"] : []), StringComparer.Ordinal)).ToList();
                foreach (string?[] pair in added)
                {
                    if (pair[1] is null || pair[0] == pair[1] || !groups.Any(g => g.Contains(pair[0] ?? "<potential>") && g.Contains(pair[1]!)))
                    { throw Mismatch(); }
                }
                foreach (HashSet<string> group in groups)
                {
                    var reached = new HashSet<string>(StringComparer.Ordinal) { group.First() };
                    for (int i = 0; i < group.Count; i++)
                    { foreach (string?[] pair in added.Where(p => group.Contains(p[0] ?? "<potential>") && group.Contains(p[1]!) && (reached.Contains(p[0] ?? "<potential>") || reached.Contains(p[1]!)))) { reached.Add(pair[0] ?? "<potential>"); reached.Add(pair[1]!); } }
                    if (!reached.SetEquals(group))
                    { throw Mismatch(); }
                }
                if (added.Count == 0 && !interruptions)
                { throw Mismatch(); }
                // Live T7b: EPLAN computes interruption cross-references only after the commit; verify below.
                return actual;
            }, _ => true);
            if (generated.Warning is not null)
            { throw Unknown(); }
            generationCommitted = true;
            if (interruptions)
            { plan["interruptionPoints"] = InterruptionPointPlanning.Verify(plan, creator.Interruptions(project, pages)); }
            plan["execution"] = ExecutionState.Committed;
            plan["dryRun"] = false;
            plan["actualEndpoints"] = JArray.FromObject(generated.Result);
            return plan;
        }
        catch (Exception failure) when (failure is not BridgeErrorException { Error.Execution: ExecutionState.Unknown })
        {
            try
            {
                new OperationContext(OperationContext.Current.RequestId, write: true, progress: null).Run(() =>
                { if (generationCommitted) { creator.Undo(project); } creator.Undo(project); return ConnectionCreatePlanner.Plan(input.ToString(Formatting.None), creator.Read(project, page), creator, fingerprint); });
            }
            catch (Exception) { throw Unknown(); }
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Connection creation failed; generation and placement were rolled back. Re-read the page before retrying.", failure)
            { Error = { Execution = ExecutionState.RolledBack } };
        }
    }
    private static BridgeErrorException Mismatch() => new(ErrorReason.EplanOperationFailed, "Actual connection geometry or logical endpoints differ from the plan.");
    private static BridgeErrorException Unknown() => new(ErrorReason.OutcomeUnknown, "Connection creation outcome is unknown; ask the engineer to inspect the project before further writes.") { Error = { Execution = ExecutionState.Unknown } };
}
