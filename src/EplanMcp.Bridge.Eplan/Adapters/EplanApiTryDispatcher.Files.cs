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
        { parameters[path.Name] = ApiTryFilePaths.Resolve(args.DataRoot, args.OutputId, path.File, path.Output, create: true); }
        return parameters;
    }
}
