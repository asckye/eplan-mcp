using Eplan.EplApi.Base;

namespace Eplan.EplApi.DataModel;

internal sealed class ReportBlock : IDisposable
{
    public string? FailField { get; set; }
    public bool Disposed { get; private set; }
    public Dictionary<string, string> Values { get; } = new(StringComparer.Ordinal)
    {
        ["filter_scheme"] = "Location designation_A3",
        ["sort_scheme"] = "DT (full)",
        ["filter_scheme_data"] = " \r\n<Filter value=\"A&B\" />\t",
        ["sort_scheme_data"] = "<Sort>\u202eDT (full)</Sort>\r\n",
        ["function_filter_scheme_data"] = "<FunctionFilter>\t1</FunctionFilter>",
        ["function_sort_scheme_data"] = "<FunctionSort>2</FunctionSort> ",
    };
    public Dictionary<string, string> Assignments { get; } = new(StringComparer.Ordinal);
    public MultiLangString Description { get; } = new("en_US@Contents;zh_CN@目录;");
    public DocumentTypeManager.DocumentType Type => Read("type", DocumentTypeManager.DocumentType.TableOfContents);
    public string FormName => Read("form", "F06_002");
    public string FilterSchemaName { get => Read("filter_scheme", Values["filter_scheme"]); set => Write("filter_scheme", value); }
    public string SortSchemaName { get => Read("sort_scheme", Values["sort_scheme"]); set => Write("sort_scheme", value); }
    public string FunctionFilterSchemaName => Read("function_filter_scheme", "Functions");
    public string FunctionSortSchemaName => Read("function_sort_scheme", "Function sort");
    public string FilterSchemaData { get => Read("filter_scheme_data", Values["filter_scheme_data"]); set => Write("filter_scheme_data", value); }
    public string SortSchemaData { get => Read("sort_scheme_data", Values["sort_scheme_data"]); set => Write("sort_scheme_data", value); }
    public string FunctionFilterSchemaData { get => Read("function_filter_scheme_data", Values["function_filter_scheme_data"]); set => Write("function_filter_scheme_data", value); }
    public string FunctionSortSchemaData { get => Read("function_sort_scheme_data", Values["function_sort_scheme_data"]); set => Write("function_sort_scheme_data", value); }
    public string DeviceTag => Read("device_tag", "=K1+A2-KE1");
    public bool IsAutomaticPageDescription => Read("automatic_page_description", true);
    public MultiLangString ManualPageDescription => Read("manual_page_description", Description);
    public void Dispose() => Disposed = true;

    private void Write(string field, string value)
    {
        Assert.False(Disposed);
        Values[field] = value;
        Assignments[field] = value;
    }

    private T Read<T>(string field, T value)
    {
        Assert.False(Disposed);
        return FailField == field ? throw new InvalidOperationException("Unreadable " + field) : value;
    }
}
