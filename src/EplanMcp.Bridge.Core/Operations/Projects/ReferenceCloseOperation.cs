using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Unregistered M0a seam; the shared lifecycle owner resolves the handle, never the current project.</summary>
public sealed class ReferenceCloseOperation(ReferenceCopies copies) : Operation<ReferenceCloseArgs, ProjectListData>
{
    public override string Name => OperationNames.ReferenceClose;
    protected override ProjectListData Execute(ReferenceCloseArgs args) => copies.Close(args);
}
