using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanApiTryDispatcher
{
    private static JObject FileParameters(ApiTryArgs args)
    {
        JObject parameters = ApiTryPlanOperation.Parameters(args.Parameters);
        foreach (var path in ApiTryFiles.Paths(args))
        {
            string full = ApiTryFilePaths.Resolve(args.DataRoot, args.OutputId, path.File, path.Output, create: true);
            if (path.Folder)
            { System.IO.Directory.CreateDirectory(full); }
            parameters[path.Name] = full;
        }
        return parameters;
    }
}
