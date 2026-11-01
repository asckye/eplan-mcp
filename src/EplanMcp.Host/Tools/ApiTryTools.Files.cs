using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Host.Tools;

internal sealed partial class ApiTryTools
{
    private ApiTryArgs FileArguments(string group, string project, JsonElement parameters)
    {
        var args = new ApiTryArgs { Group = group, Project = project, Parameters = parameters.GetRawText(), DataRoot = (locations ?? HostDataLocations.FromEnvironment()).Root, OutputId = Guid.NewGuid().ToString("N") };
        string[]? shape = ApiTryCatalog.Shape(group, parameters.EnumerateObject().Select(p => p.Name));
        if (shape is not null && parameters.EnumerateObject().Any(p => ApiTryCatalog.PathDirection(shape[1], p.Name) is { } direction && !ApiTryFilePaths.Valid(p.Value.GetString()!, direction == "output")))
        { throw new BridgeCallException(new BridgeError { Reason = ErrorReason.InvalidArgument, Message = "Input files must be relative to Host imports (260 characters maximum); output file names are limited to 100 characters, without links or reserved names." }); }
        return args;
    }
}
