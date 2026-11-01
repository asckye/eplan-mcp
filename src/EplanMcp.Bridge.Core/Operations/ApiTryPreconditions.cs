using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Hand-kept page-context requirements; evidence and parameter names: api-notes A64.</summary>
public static class ApiTryPreconditions
{
    private static readonly Dictionary<string, (string? Parameter, bool ExplicitPage)> _pageBound = new(StringComparer.Ordinal)
    {
        ["NameService.CanDeviceTagBeNested"] = ("pFunction", true),
        ["NameService.FindNameGivingFunction"] = ("pActual", true),
        ["NameService.FindNameGivingObject"] = ("f", true),
        ["NameService.UpdateConnectionDefinitionPointsParent"] = ("pCDP", true),
        ["NameService.AdjustFullName"] = ("pFunctionBase", true),
        ["NameService.AdjustVisibleName"] = ("pFunctionBase", true),
        ["NameService.EvaluateAndSetAllNames"] = (null, true),
        ["NameService.EvaluateAndSetAllVisibleNames"] = (null, true),
        ["Reports.Update"] = ("oReportBlockRef", false),
    };

    public static void Check(string group, string[] shape, Func<string, bool> hasPage)
    {
        const string prefix = "O:Eplan.EplApi.HEServices.";
        if (!group.StartsWith(prefix, StringComparison.Ordinal) || !_pageBound.TryGetValue(group.Substring(prefix.Length), out var rule))
        { return; }
        string? parameter = rule.Parameter;
        if (!rule.ExplicitPage && parameter is not null && !shape.Skip(2).Contains(parameter, StringComparer.Ordinal))
        { return; } // Reports.Update(Project/Page) have no ReportBlockReference precondition.
        if (parameter is not null && !hasPage(parameter))
        { throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.InvalidArgument, "This member requires a placed object on a valid page; discover a placed object before retrying. Nothing changed.")); }
        // Each generated service call constructs a fresh instance with no Page (A63/A64).
        if (rule.ExplicitPage && !shape.Skip(2).Any(n => ApiTryCatalog.ParameterShape(shape[1], n)?[0] == "Eplan.EplApi.DataModel.Page"))
        { throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.InvalidArgument, "This member requires an explicit Page overload; supply its page locator. Nothing changed.")); }
    }
}
