using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>A84: reads detached settings from a fresh page wrapper outside a write transaction.</summary>
internal static class EplanReportReader
{
    internal static PageReport? Read(Page page, Action? unreadable = null) => Field("report", () =>
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
                FilterSchemeData = Field("filter_scheme_data", () => block.FilterSchemaData),
                SortSchemeData = Field("sort_scheme_data", () => block.SortSchemaData),
                FunctionFilterSchemeData = Field("function_filter_scheme_data", () => block.FunctionFilterSchemaData),
                FunctionSortSchemeData = Field("function_sort_scheme_data", () => block.FunctionSortSchemaData),
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
    }, unreadable);

    private static T? Field<T>(string name, Func<T> read, Action? unreadable = null)
    {
        try
        {
            return read();
        }
        catch (Exception error)
        {
            OperationContext.Current.Warn("Could not read report field " + name + ".", error);
            unreadable?.Invoke();
            return default;
        }
    }
}
