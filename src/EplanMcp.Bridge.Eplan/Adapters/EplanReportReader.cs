using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>A84: reads a fresh page wrapper outside a write transaction; no scheme data or borrowed native objects escape.</summary>
internal static class EplanReportReader
{
    internal static PageReport? Read(Page page) => Field("report", () =>
    {
        using ReportBlock? block = page.ReportBlock;
        if (block is null)
        {
            return null;
        }
        try
        {
            return new PageReport
            {
                Type = Field("type", () => block.Type.ToString()),
                Form = Field("form", () => block.FormName),
                FilterScheme = Field("filter_scheme", () => block.FilterSchemaName),
                SortScheme = Field("sort_scheme", () => block.SortSchemaName),
                FunctionFilterScheme = Field("function_filter_scheme", () => block.FunctionFilterSchemaName),
                FunctionSortScheme = Field("function_sort_scheme", () => block.FunctionSortSchemaName),
                DeviceTag = Field("device_tag", () => block.DeviceTag),
                AutomaticPageDescription = Field<bool?>("automatic_page_description", () => block.IsAutomaticPageDescription),
                ManualPageDescription = Field("manual_page_description", () =>
                {
                    using MultiLangString text = block.ManualPageDescription;
                    return text?.GetAsString();
                }),
            };
        }
        finally
        {
            GC.KeepAlive(block);
            GC.KeepAlive(page); // A80: owners remain reachable through the final native field read.
        }
    });

    private static T? Field<T>(string name, Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception error)
        {
            OperationContext.Current.Warn("Could not read report field " + name + ".", error);
            return default;
        }
    }
}
