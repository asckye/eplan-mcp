using System.Collections.Specialized;
using System.Text.RegularExpressions;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// PDF redraw 2026-11-06: a report's form must be in the project; a basic project may not bring all of them (S029153).
/// As the report dialog does, a form from the system's form directory is copied into the project. System master data is only read.
/// </summary>
internal static class EplanReportForms
{
    private static readonly Regex _number = new(@"(?:^|_)F(\d{2})_", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static void Ensure(Project project, string form)
    {
        Match number = _number.Match(form);
        if (!number.Success)
        { return; }
        string file = form + ".f" + number.Groups[1].Value;
        using var masterdata = new Masterdata();
        foreach (string? entry in masterdata.get_ProjectEntries(project))
        {
            if (string.Equals(entry, file, StringComparison.OrdinalIgnoreCase))
            { return; }
        }
        if (!File.Exists(PathMap.SubstitutePath(Path.Combine("$(MD_FORMS)", file))))
        { return; }
        System.Collections.Hashtable errors = masterdata.AddToProjectEx(project, new StringCollection { file });
        if (errors.Count > 0)
        { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN could not copy form '" + form + "' into the project, so no report was created."); }
    }
}
