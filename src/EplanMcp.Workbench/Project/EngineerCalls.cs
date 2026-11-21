using System.Text.Json.Nodes;
using EplanMcp.Contracts;
using EplanMcp.Workbench.Protocol;

namespace EplanMcp.Workbench.Project;

/// <summary>
/// One tool call the engineer starts in the Workbench (ADR-0065 decision 3): the tool, the arguments as sent, and the
/// hash the Host will put in its approval question. The hash is of the same <c>EplanMcp.Contracts</c> object the
/// Host's tool builds from these arguments; the Host tests run each real tool and compare.
/// </summary>
internal sealed record EngineerCall(string Tool, JsonObject Arguments, string ArgumentsHash);

/// <summary>The calls behind the project page's buttons. Each builds the arguments and the Host's argument object from the same values.</summary>
internal static class EngineerCalls
{
    /// <summary>Updates every report of <paramref name="project"/>.</summary>
    public static EngineerCall ReportUpdate(string project) =>
        new("eplan_report_update", new JsonObject { ["project"] = project }, ArgumentsHash.Of(new ReportUpdateArgs { Project = project }));

    /// <summary>Runs EPLAN's check with its current scheme, on <paramref name="page"/> or, when null, the whole project.</summary>
    public static EngineerCall CheckRun(string project, string? page)
    {
        var arguments = new JsonObject { ["project"] = project };
        if (page is not null)
        {
            arguments["pages"] = new JsonArray(page);
        }

        return new("eplan_check_run", arguments, ArgumentsHash.Of(new CheckRunArgs { Project = project, Pages = page is null ? null : [page] }));
    }

    /// <summary>Exports <paramref name="page"/> or, when null, the whole project as PDF or DXF; an export asks no approval (ADR-0012).</summary>
    public static EngineerCall Export(string project, string? page, bool dxf)
    {
        var arguments = new JsonObject { ["project"] = project };
        if (page is not null)
        {
            arguments["pages"] = new JsonArray(page);
        }

        return new(dxf ? "eplan_export_dxf" : "eplan_export_pdf", arguments, string.Empty);
    }
}
