using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Unregistered M0a seam; copies and opens only the Host's new scratch reference.</summary>
public sealed class ReferencePrepareOperation(ReferenceCopies copies) : Operation<ReferencePrepareArgs, ReferenceCopyData>
{
    public override string Name => OperationNames.ReferencePrepare;
    protected override ReferenceCopyData Execute(ReferencePrepareArgs args) => copies.Prepare(args);
}
