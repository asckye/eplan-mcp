using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A55: commit native creation before readback; compensate only confirmed commits using undo.</summary>
internal static class CableDefinitionExecution
{
    internal static JObject Run(IConnectionCreator creator, IConnectionGenerator generator, ITransactionScopeFactory transactions,
        ConnectionCreatePlanner.Scene scene, JObject input, JObject plan)
    {
        string project = scene.Project.Path, page = scene.Page.Page;
        if (creator is not ICableDefinitionWriter writer || plan["interruptionScope"] is not null)
        { throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.CapabilityMissing, "Use cable definitions without interruption points and a cable-capable Bridge.")); }
        Action definitions;
        Action? symbols;
        JObject before;
        try
        {
            ConnectionCreatePlanner.Require(plan["logicalAfterGenerate"]!.All(g => g["pins"]!.Count() == 2 && (bool?)g["potentialStartIsNull"] == false), "/items", "Cable calls require explicit two-pin logical paths.");
            definitions = writer.PrepareCables(project, page, input, plan);
            symbols = plan["symbols"]!.Any() ? creator.Prepare(project, page, (JArray)plan["symbols"]!) : null;
            before = CableDefinitionRestoration.Capture(scene);
        }
        catch (BridgeErrorException ex) { throw WriteArguments.NotStarted(ex); }
        int commits = 0;
        void Commit(ITransactionScopeFactory factory, Action action, bool createsObjects = false)
        {
            Committed<bool> result;
            try
            { result = TransactionRunner.Run(factory, "eplan_connection_create", () => { action(); return true; }, _ => true); }
            // Live T8b: Abort can leave created objects behind. Do not inspect their property lists (A55).
            catch (Exception failure) when (createsObjects)
            { throw Unknown(failure); }
            if (result.Warning is not null)
            { throw Unknown(new InvalidOperationException(result.Warning)); }
            commits++;
        }
        void Generate() => Commit(transactions, () => generator.Generate(project, [page]));
        string Segments(IEnumerable<double[][]> values) => string.Join(";", values.Select(s => string.Join("|", s.Select(p => new JArray(p).ToString()).OrderBy(p => p, StringComparer.Ordinal))).Distinct(StringComparer.Ordinal).OrderBy(s => s, StringComparer.Ordinal));
        string geometry = Segments(scene.Graphical.Concat(plan["graphicalAutoconnect"]!.ToObject<List<double[][]>>()!));
        try
        {
            if (symbols is not null)
            { Commit(transactions, symbols); }
            if (plan["graphicalAutoconnect"]!.Any())
            { Generate(); }
            string Endpoints(IEnumerable<string?[]> pairs) => string.Join(";", pairs.Select(p => string.Join("|", p.OrderBy(s => s, StringComparer.Ordinal))).OrderBy(s => s, StringComparer.Ordinal));
            string baseline = Endpoints(creator.Endpoints(project, page));
            if (Segments(creator.Read(project, page).Graphical) != geometry)
            { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Cable wire geometry differs from the plan."); }
            if (baseline != Endpoints(scene.Logical.Select(c => new[] { c.StartDevice, c.EndDevice }).Concat(plan["logicalAfterGenerate"]!.Select(g => g["pins"]!.Values<string>().ToArray()))))
            { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Generated cable paths differ from planned endpoints."); }
            Commit(creator.PlacementTransactions(project), definitions, createsObjects: true);
            // A55: each readback discovers fresh native objects after the transaction has ended.
            writer.VerifyCables(project, page, input, plan);
            Generate();
            Commit(creator.PlacementTransactions(project), () => writer.BindCables(project, page, input, plan));
            writer.VerifyBindings(project, page, input, plan);
            Generate();
            Commit(creator.PlacementTransactions(project), () => writer.AssignCores(project, page, input, plan), createsObjects: true);
            CableDefinitionPlanner.VerifyCores(plan, writer.ReadCores(project, page, input, plan), page, input);
            Generate();
            // Membership and all final properties are checked after the last commit, never after Abort (A55).
            plan["actualCores"] = writer.ReadCores(project, page, input, plan);
            CableDefinitionPlanner.VerifyCores(plan, (JArray)plan["actualCores"]!, page, input);
            if (Endpoints(creator.Endpoints(project, page)) != baseline || Segments(creator.Read(project, page).Graphical) != geometry)
            { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Cable generation changed geometry or logical endpoints."); }
            plan["undoSteps"] = commits;
            plan["execution"] = ExecutionState.Committed;
            plan["dryRun"] = false;
            plan.Remove("_cableBefore");
            return plan;
        }
        catch (Exception failure) when (failure is not BridgeErrorException { Error.Execution: ExecutionState.Unknown })
        {
            try
            {
                new OperationContext(OperationContext.Current.RequestId, true, null).Run(() =>
                {
                    for (int i = 0; i < commits; i++)
                    { creator.Undo(project); }
                    CableDefinitionRestoration.Verify(before, CableDefinitionRestoration.Capture(creator.Read(project, page)));
                    return true;
                });
            }
            catch (Exception compensation) { throw Unknown(new AggregateException(failure, compensation)); }
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Cable creation failed; all commits from this call were undone. Re-read the page before retrying.", failure)
            { Error = { Execution = ExecutionState.RolledBack } };
        }
    }
    // The cause travels as the inner exception so the Bridge log shows why the outcome became unknown.
    private static BridgeErrorException Unknown(Exception cause) => new(ErrorReason.OutcomeUnknown, "Cable outcome is unknown; ask the engineer to inspect the project before further writes.", cause) { Error = { Execution = ExecutionState.Unknown } };
}
