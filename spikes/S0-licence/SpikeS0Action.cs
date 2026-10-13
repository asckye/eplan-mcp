using System.Diagnostics;
using System.Globalization;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S0;

/// <summary>
/// Spike S0 (licence). Answers: does an UNSIGNED compiled add-in load under this licence, and can it
/// read and modify a project larger than 5 pages? Every probe records success or the exact exception.
/// <para>
/// Usage: <c>EplanMcpSpikeS0 /Report:"C:\…\s0-report.json" /ScratchRoot:"C:\…\scratch" /Write:1</c>.
/// Write probes run only when the current project lies under ScratchRoot.
/// </para>
/// </summary>
public sealed class SpikeS0Action : IEplAction
{
    public const string ActionName = "EplanMcpSpikeS0";

    private const string PageLocation = "MCPS0";

    public bool OnRegister(ref string Name, ref int Ordinal)
    {
        Name = ActionName;
        Ordinal = 20;
        return true;
    }

    public void GetActionProperties(ref ActionProperties actionProperties)
    {
    }

    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        string reportPath = GetParameter(oActionCallingContext, "Report");
        string scratchRoot = GetParameter(oActionCallingContext, "ScratchRoot");
        bool write = GetParameter(oActionCallingContext, "Write") == "1";

        var report = new JObject
        {
            ["spike"] = "S0-licence",
            ["timestamp"] = DateTime.Now.ToString("o", CultureInfo.InvariantCulture),
            ["process"] = new JObject
            {
                ["pid"] = Process.GetCurrentProcess().Id,
                ["clr"] = Environment.Version.ToString(),
                ["temp"] = Environment.GetEnvironmentVariable("TEMP"),
            },
            ["assembly"] = DescribeAssembly(),
        };

        using (new QuietModeStep(QuietModes.ShowNoDialogs))
        {
            Probe(report, "licence", DescribeLicence);

            Project? project = null;
            Probe(report, "currentProject", () =>
            {
                project = new SelectionSet().GetCurrentProject(false);
                return project is null
                    ? new JObject { ["open"] = false }
                    : new JObject { ["open"] = true, ["name"] = project.ProjectName, ["fullName"] = project.ProjectFullName };
            });

            if (project is not null)
            {
                Project p = project;
                Probe(report, "readPages", () => ReadPages(p));
                Probe(report, "readFunctions", () => ReadFunctions(p));

                string? refusal = WriteRefusal(p, write, scratchRoot);
                if (refusal is null)
                {
                    Probe(report, "writeCreatePageCommitted", () => CreatePage(p, commit: true));
                    Probe(report, "writeRemovePage", () => RemoveSpikePages(p));
                    Probe(report, "rollbackUncommittedCreate", () => CreatePage(p, commit: false));
                    Probe(report, "pagesAfterAll", () => ReadPages(p));
                }
                else
                {
                    report["writeProbes"] = "skipped: " + refusal;
                }
            }
        }

        if (!string.IsNullOrEmpty(reportPath))
        {
            File.WriteAllText(reportPath, report.ToString(Formatting.Indented));
        }

        oActionCallingContext.AddParameter("S0Result", Summarize(report));
        return true;
    }

    private static string GetParameter(ActionCallingContext context, string name)
    {
        string value = string.Empty;
        context.GetParameter(name, ref value);
        return value ?? string.Empty;
    }

    private static void Probe(JObject report, string name, Func<JToken> probe)
    {
        var watch = Stopwatch.StartNew();
        JObject entry;
        try
        {
            entry = new JObject { ["ok"] = true, ["result"] = probe() };
        }
        catch (Exception ex)
        {
            entry = new JObject { ["ok"] = false, ["error"] = DescribeException(ex) };
        }

        entry["ms"] = watch.ElapsedMilliseconds;
        report[name] = entry;
    }

    private static JObject DescribeException(Exception ex)
    {
        var o = new JObject
        {
            ["type"] = ex.GetType().FullName,
            ["message"] = ex.Message,
        };
        if (ex.InnerException is not null)
        {
            o["inner"] = DescribeException(ex.InnerException);
        }

        return o;
    }

    private static JObject DescribeAssembly()
    {
        var assembly = typeof(SpikeS0Action).Assembly;
        var name = assembly.GetName();
        byte[]? token = name.GetPublicKeyToken();
        return new JObject
        {
            ["name"] = name.Name,
            ["version"] = name.Version?.ToString(),
            ["strongNamed"] = token is { Length: > 0 },
            ["location"] = assembly.Location,
        };
    }

    private static JToken DescribeLicence()
    {
        var licence = new License();
        try
        {
            return new JObject
            {
                ["type"] = licence.Type.ToString(),
                ["APIExtension"] = licence.HasOption(LicenseOptions.APIExtension),
                ["APIExtensionPartsmanagement"] = licence.HasOption(LicenseOptions.APIExtensionPartsmanagement),
            };
        }
        finally
        {
            (licence as IDisposable)?.Dispose();
        }
    }

    private static JToken ReadPages(Project project)
    {
        Page[] pages = project.Pages;
        return new JObject
        {
            ["count"] = pages.Length,
            ["first"] = new JArray(pages.Take(3).Select(page => page.Name)),
            ["spikePages"] = new JArray(pages.Where(IsSpikePage).Select(page => page.Name)),
        };
    }

    private static JToken ReadFunctions(Project project)
    {
        Function[] functions = new DMObjectsFinder(project).GetFunctions(null);
        return new JObject
        {
            ["count"] = functions.Length,
            ["first"] = new JArray(functions.Take(3).Select(function => function.Name)),
        };
    }

    private static string? WriteRefusal(Project project, bool write, string scratchRoot)
    {
        if (!write)
        {
            return "/Write:1 not given";
        }

        if (string.IsNullOrEmpty(scratchRoot))
        {
            return "/ScratchRoot not given";
        }

        string root = Path.GetFullPath(scratchRoot).TrimEnd('\\') + "\\";
        string projectPath = Path.GetFullPath(project.ProjectFullName);
        return projectPath.StartsWith(root, StringComparison.OrdinalIgnoreCase)
            ? null
            : "project is outside the scratch root";
    }

    private static JToken CreatePage(Project project, bool commit)
    {
        int before = project.Pages.Length;
        string createdName;

        UndoStep undo = new UndoManager().CreateUndoStep();
        undo.SetUndoDescription("EplanMcp spike S0");
        using (SafetyPoint safetyPoint = SafetyPoint.Create())
        {
            var properties = new PagePropertyList();
            properties.DESIGNATION_LOCATION = PageLocation;
            properties.PAGE_COUNTER = commit ? 1 : 2;

            var page = new Page();
            page.Create(project, DocumentTypeManager.DocumentType.Circuit, properties);
            createdName = page.Name;

            if (commit)
            {
                safetyPoint.Commit();
            }
        }

        undo.CloseUndo();

        Page[] after = project.Pages;
        return new JObject
        {
            ["committed"] = commit,
            ["createdName"] = createdName,
            ["pagesBefore"] = before,
            ["pagesAfter"] = after.Length,
            ["stillExists"] = after.Any(page => page.Name == createdName),
        };
    }

    private static JToken RemoveSpikePages(Project project)
    {
        var removed = new JArray();
        using (SafetyPoint safetyPoint = SafetyPoint.Create())
        {
            foreach (Page page in project.Pages.Where(IsSpikePage).ToArray())
            {
                removed.Add(page.Name);
                page.Remove();
            }

            safetyPoint.Commit();
        }

        return new JObject { ["removed"] = removed, ["pagesAfter"] = project.Pages.Length };
    }

    private static bool IsSpikePage(Page page) =>
        page.Name.IndexOf("+" + PageLocation + "/", StringComparison.Ordinal) >= 0;

    private static string Summarize(JObject report)
    {
        var parts = report.Properties()
            .Where(property => property.Value is JObject entry && entry["ok"] is not null)
            .Select(property => property.Name + "=" + (((bool?)property.Value["ok"]) == true ? "ok" : "FAIL"));
        return string.Join(";", parts);
    }
}
