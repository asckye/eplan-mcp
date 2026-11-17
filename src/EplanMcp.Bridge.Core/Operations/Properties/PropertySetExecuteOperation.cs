using System.Diagnostics;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>B1 executor; the Bridge dispatches once under LockingStep. Host tool authorization remains separate.</summary>
public sealed class PropertySetExecuteOperation(Func<string, IPropertySetSession> open,
    ITransactionScopeFactory transactions, int version, Func<TimeSpan>? elapsed = null) : Operation<PropertySetArgs, PropertySetData>, IWriteOperation
{
    public const string Tool = "eplan_property_set";
    public override string Name => OperationNames.PropertySetExecute;

    protected override PropertySetData Execute(PropertySetArgs args)
    {
        var watch = Stopwatch.StartNew();
        bool started = false;
        try
        {
            using IPropertySetSession session = open(args.Project);
            if (string.IsNullOrEmpty(args.Approved.Plan.Fingerprint))
            {
                throw InvalidArgument("Supply the complete approved property plan and fingerprint.");
            }

            PropertySetPlan current = Plan();
            PropertySetPlanner.RequireCurrent(args.Approved, current);
            var data = new PropertySetData { Plan = current.Plan };
            if (current.Plan.Changes.Count == 0)
            {
                return data;
            }

            Committed<bool> committed = TransactionRunner.Run(transactions, Tool, () =>
            {
                started = true;
                // Release lists before commit, so a release failure can still roll back the whole write.
                using (session)
                {
                    foreach (PropertySetGuard guard in current.Guards.Where(g => !JToken.DeepEquals(
                        BridgeJson.FromObject(g.Before), BridgeJson.FromObject(g.After))))
                    {
                        Checkpoint();
                        session.Write(guard);
                        Checkpoint();
                    }

                    PropertySetPlan after;
                    try
                    {
                        after = Plan();
                    }
                    catch (BridgeErrorException ex) when (ex.Error.Reason is not (ErrorReason.Cancelled or ErrorReason.Timeout or ErrorReason.LicenceMissing))
                    {
                        throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Property readback could not verify the planned scope. Re-plan before retrying.", ex);
                    }
                    foreach (PropertySetGuard guard in current.Guards)
                    {
                        bool changing = !JToken.DeepEquals(BridgeJson.FromObject(guard.Before), BridgeJson.FromObject(guard.After));
                        string[] clearing = guard.After.Where(t => t.Text.Length == 0
                            && PropertyValueComparison.TextIn(guard.Before, t.Language).Length > 0).Select(t => t.Language).ToArray();
                        guard.Before = guard.After;
                        PropertySetGuard? actual = after.Guards.SingleOrDefault(g => g.PropertyId == guard.PropertyId
                            && g.Target.Type == guard.Target.Type && g.Target.Oid == guard.Target.Oid);
                        if (changing && actual is not null)
                        {
                            guard.After.RemoveAll(t => clearing.Contains(t.Language) && !actual.Before.Any(a => a.Language == t.Language));
                            guard.WasEmpty = guard.After.All(t => t.Text.Length == 0) && actual.WasEmpty;
                        }
                    }

                    if (!JToken.DeepEquals(BridgeJson.FromObject(current.Guards), BridgeJson.FromObject(after.Guards)))
                    {
                        throw new BridgeErrorException(ErrorReason.EplanOperationFailed,
                            "Property readback or a related-object guard differs. Nothing was committed; re-plan before retrying.");
                    }
                }

                Checkpoint();
                return true;
            });
            data.Execution = ExecutionState.Committed;
            data.Undo = TransactionRunner.UndoPrefix + Tool;
            data.Warning = committed.Warning;
            return data;

            PropertySetPlan Plan()
            {
                Checkpoint();
                PropertySetPlan plan = new PropertySetPlanner(session).Plan(args.Project, version, args.Changes,
                    session.Functions, session.Languages, CancellationToken.None);
                Checkpoint();
                return plan;
            }
        }
        catch (BridgeErrorException ex) when (!started)
        {
            throw WriteArguments.NotStarted(ex);
        }
        catch (Exception ex) when (!started)
        {
            throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.EplanOperationFailed,
                "Property execution could not start. Read the project and re-plan before retrying.", ex));
        }

        void Checkpoint()
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            TimeSpan duration = elapsed?.Invoke() ?? watch.Elapsed;
            if (duration >= TimeSpan.FromSeconds(2))
            {
                OperationContext.Current.ReportProgress(0, message: "Checking property writes");
            }

            if (duration >= TimeSpan.FromSeconds(30))
            {
                throw new BridgeErrorException(ErrorReason.Timeout, "Property execution exceeded 30 seconds. Reduce changes.");
            }
        }
    }
}
