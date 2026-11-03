using Eplan.EplApi.Base;

namespace Eplan.EplApi.DataModel;

internal sealed class ReportBlock : IDisposable
{
    public string? FailField { get; set; }
    public bool Disposed { get; private set; }
    public MultiLangString Description { get; } = new("en_US@Contents;zh_CN@目录;");
    public DocumentTypeManager.DocumentType Type => Read("type", DocumentTypeManager.DocumentType.TableOfContents);
    public string FormName => Read("form", "F06_002");
    public string FilterSchemaName => Read("filter_scheme", "Location designation_A3");
    public string SortSchemaName => Read("sort_scheme", "DT (full)");
    public string FunctionFilterSchemaName => Read("function_filter_scheme", "Functions");
    public string FunctionSortSchemaName => Read("function_sort_scheme", "Function sort");
    public string DeviceTag => Read("device_tag", "=K1+A2-KE1");
    public bool IsAutomaticPageDescription => Read("automatic_page_description", true);
    public MultiLangString ManualPageDescription => Read("manual_page_description", Description);
    public void Dispose() => Disposed = true;

    private T Read<T>(string field, T value)
    {
        Assert.False(Disposed);
        return FailField == field ? throw new InvalidOperationException("Unreadable " + field) : value;
    }
}
