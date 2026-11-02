using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Core.Operations;
/// <summary>Read-only wrapper around the shared primitive preflight; never enters an outcome write record.</summary>
public sealed class PlacementTextCreatePlanOperation(PlacementTextCreateExecuteOperation preflight) : IOperation
{
    public string Name => OperationNames.PlacementTextCreatePlan;
    public object ParseArgs(JToken? args) => preflight.ParseArgs(args);
    public object? Execute(object args) => preflight.Execute(args);
}
