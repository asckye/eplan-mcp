using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;
/// <summary>File evidence is captured inside the existing trial transaction, before commit.</summary>
internal static class ApiTryFiles
{
    /// <summary>Path parameters; an output folder (<c>output_directory</c>) is an output that is created as a folder.</summary>
    internal static IEnumerable<(string Name, string File, bool Output, bool Folder)> Paths(ApiTryArgs args)
    {
        JObject parameters = ApiTryPlanOperation.Parameters(args.Parameters);
        string[]? shape = ApiTryCatalog.Shape(args.Group, parameters.Properties().Select(p => p.Name));
        return shape is null ? [] : parameters.Properties().Select(p => (Parameter: p, Direction: ApiTryCatalog.PathDirection(shape[1], p.Name)))
            .Where(p => p.Direction is not null).Select(p => (p.Parameter.Name, (string)p.Parameter.Value!, p.Direction != "input", p.Direction == "output_directory"));
    }
    internal static Dictionary<string, string> Observe(ApiTryArgs args, Dictionary<string, string> state, bool planning = false)
    {
        try
        {
            foreach (var path in Paths(args))
            {
                state["files:destination"] = Fingerprint.Of([args.DataRoot, args.OutputId]);
                if (path.Output && !planning)
                { continue; }
                string full = ApiTryFilePaths.Resolve(args.DataRoot, args.OutputId, path.File, path.Output);
                if (!path.Output)
                { state["input:" + path.Name] = Newtonsoft.Json.JsonConvert.SerializeObject(ApiTryFilePaths.Describe(full, path.File)); }
            }
            return state;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { throw new BridgeErrorException(ErrorReason.InvalidArgument, "Trial file validation failed: use bounded files in Host imports and a new export destination, without links.", ex); }
    }
    internal static List<ApiTryFile> Outputs(ApiTryArgs args)
    {
        if (!Paths(args).Any(p => p.Output))
        { return []; }
        List<ApiTryFile> files = ApiTryFilePaths.Outputs(args.DataRoot, args.OutputId);
        return files.Count > 0 ? files : throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "The trial produced no output files.");
    }
}
