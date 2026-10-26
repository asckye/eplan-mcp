using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Plans only B1 candidates through the Bridge; no Host tool permission is granted.</summary>
public sealed class PropertySetPlanOperation(Func<string, IPropertySetSession> open, int version)
    : Operation<PropertySetArgs, PropertySetPlan>
{
    public override string Name => OperationNames.PropertySetPlan;

    protected override PropertySetPlan Execute(PropertySetArgs args)
    {
        using IPropertySetSession session = open(args.Project);
        return new PropertySetPlanner(session).Plan(args.Project, version, args.Changes,
            session.Functions, session.Languages, CancellationToken.None);
    }
}
