using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

[Trait("Size", "Small")]
public sealed class ReportSchemeWriteTests
{
    [Fact]
    public void ApplySchemes_ProvidedNamesAndRawData_SetNativeFields()
    {
        using var block = new ReportBlock();
        var args = new ReportCreateArgs
        {
            FilterScheme = "Location designation_A3",
            SortScheme = "DT (full)",
            FilterSchemeData = " \r\n<Filter value=\"A&B\" />\t",
            SortSchemeData = "<Sort>\u202eDT (full)</Sort>\r\n",
            FunctionFilterSchemeData = new string('x', 4001),
            FunctionSortSchemeData = "<FunctionSort>2</FunctionSort> ",
        };

        EplanReportCreator.ApplySchemes(block, args);

        Assert.Equal(args.FilterScheme, block.FilterSchemaName);
        Assert.Equal(args.SortScheme, block.SortSchemaName);
        Assert.Equal(args.FilterSchemeData, block.FilterSchemaData);
        Assert.Equal(args.SortSchemeData, block.SortSchemaData);
        Assert.Equal(args.FunctionFilterSchemeData, block.FunctionFilterSchemaData);
        Assert.Equal(args.FunctionSortSchemeData, block.FunctionSortSchemaData);
        Assert.Equal(6, block.Assignments.Count);
    }

    [Fact]
    public void ApplySchemes_OmittedValues_LeaveNativeFieldsUnchanged()
    {
        using var block = new ReportBlock();
        KeyValuePair<string, string>[] before = [.. block.Values];

        EplanReportCreator.ApplySchemes(block, new ReportCreateArgs());

        Assert.Empty(block.Assignments);
        Assert.Equal(before, block.Values);
    }

    [Fact]
    public void ApplySchemes_EmptyData_IsAssigned()
    {
        using var block = new ReportBlock();
        var args = new ReportCreateArgs
        {
            FilterSchemeData = string.Empty,
            SortSchemeData = string.Empty,
            FunctionFilterSchemeData = string.Empty,
            FunctionSortSchemeData = string.Empty,
        };

        EplanReportCreator.ApplySchemes(block, args);

        Assert.Equal(4, block.Assignments.Count);
        Assert.All(block.Assignments.Values, value => Assert.Empty(value));
    }
}
