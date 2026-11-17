using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Per-request decorator used only by plan.step.execute; never shared with single-step tools.</summary>
internal sealed class PlanUndoScopeFactory(ITransactionScopeFactory inner, PlanStepExecuteArgs args, string operation)
    : ITransactionScopeFactory
{
    public ITransactionScope Begin(string undoDescription) => inner.Begin(string.Format(CultureInfo.InvariantCulture,
        "AI: plan {0} {1}/{2} {3}", args.PlanId.Substring(0, 8), args.Index, args.Total, operation));
}
