using Eplan.EplApi.DataModel;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanReportCreator
{
    internal static void ApplySchemes(ReportBlock block, ReportCreateArgs args)
    {
        if (args.FilterScheme is not null)
        { block.FilterSchemaName = args.FilterScheme; }
        if (args.SortScheme is not null)
        { block.SortSchemaName = args.SortScheme; }
        // A84: source-project names may be unknown here; nonempty data carries the actual scheme.
        if (args.FilterSchemeData is not null)
        { block.FilterSchemaData = args.FilterSchemeData; }
        if (args.SortSchemeData is not null)
        { block.SortSchemaData = args.SortSchemeData; }
        if (args.FunctionFilterSchemeData is not null)
        { block.FunctionFilterSchemaData = args.FunctionFilterSchemeData; }
        if (args.FunctionSortSchemeData is not null)
        { block.FunctionSortSchemaData = args.FunctionSortSchemeData; }
    }
}
