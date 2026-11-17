using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>PLC schematic generation, implemented by the EPLAN adapter layer. Main thread only.</summary>
public interface IPlcSchematics
{
    /// <summary>The PLC devices of the project's schematic configuration.</summary>
    public IReadOnlyList<PlcSchematicDevice> Devices(string projectPath);

    /// <summary>A13: generation places functions, so it runs in a Transaction scope.</summary>
    public ITransactionScopeFactory Transactions(string projectPath);

    /// <summary>Generates the devices' pages inside the write's transaction; returns their names.</summary>
    public IReadOnlyList<string> Generate(string projectPath, IReadOnlyList<PlcGenerateDevice> devices);

    /// <summary>Reads the generated pages back after the commit.</summary>
    public IReadOnlyList<PlcGeneratedPage> ReadPages(string projectPath, IReadOnlyList<string> pages);
}

/// <summary>The checked arguments of eplan_plc_generate.</summary>
internal sealed class PlcGenerateRequest
{
    public const int MaxDevices = 50;

    private PlcGenerateRequest(OpenProject project, List<PlcGenerateDevice> devices, List<string> pages)
    {
        Project = project;
        Devices = devices;
        Pages = pages;
    }

    public OpenProject Project { get; }

    /// <summary>The devices with their full macro paths or the macro EPLAN knows.</summary>
    public List<PlcGenerateDevice> Devices { get; }

    public List<string> Pages { get; }

    public static IEnumerable<string> PagesOf(PlcGenerateDevice device) =>
        new[] { device.SingleLinePage, device.MultiLinePage, device.OverviewPage, device.RackPage }.Where(p => p is not null).Select(p => p!);

    public static PlcGenerateRequest Check(PlcGenerateArgs args, IProjectCatalog catalog, IPageReader reader, IPlcSchematics schematics, IMacroReader macros)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        List<PlcGenerateDevice> given = args.Devices ?? [];
        if (given.Count is < 1 or > MaxDevices)
        { violations.Add(FormattableString.Invariant($"'devices' must hold 1 to {MaxDevices} PLC devices.")); }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < given.Count; i++)
        {
            string at = "devices[" + i.ToString(CultureInfo.InvariantCulture) + "]";
            PlcGenerateDevice device = given[i];
            if (string.IsNullOrEmpty(device.Tag))
            { violations.Add(at + "/tag is required: a full tag from eplan_device_list with category PLCBox."); }
            else if (!seen.Add(device.Tag!))
            { violations.Add(at + "/tag repeats another device."); }
            string[] pages = [.. PagesOf(device)];
            if (pages.Length == 0)
            { violations.Add(at + ": give at least one of single_line_page, multi_line_page, overview_page and rack_page."); }
            violations.AddRange(pages.Where(p => !p.Contains('/') || p.Any(char.IsControl)).Select(p => at + ": '" + p + "' is not a full page name."));
            // Two macros on one page would lie on top of each other, so every page has one device.
            violations.AddRange(pages.Where(p => !targets.Add(p)).Select(p => at + ": page '" + p + "' is given twice; every device and representation needs its own new page."));
        }
        if (violations.Count > 0)
        { throw WriteArguments.Invalid(violations); }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        IReadOnlyList<string> names = reader.PageNames(project.Path);
        violations.AddRange(targets.Where(p => PageUpdateRequest.Find(names, p) is not null).Select(p => "Page '" + p + "' already exists: generation places on new pages, because it has no way to tell what it would cover."));
        // EPLAN answers an unknown device with a dialog, so it is refused here.
        IReadOnlyList<PlcSchematicDevice> known = schematics.Devices(project.Path);
        var devices = new List<PlcGenerateDevice>();
        foreach (PlcGenerateDevice device in given)
        {
            PlcSchematicDevice? found = known.FirstOrDefault(d => string.Equals(d.Name, device.Tag, StringComparison.OrdinalIgnoreCase));
            if (found is null)
            {
                violations.Add("'" + device.Tag + "' is not a PLC device of the project. Known: " + string.Join(", ", known.Take(20).Select(d => d.Name)) + ". Insert its overview macro first (eplan_macro_insert).");
                continue;
            }
            string? macro = device.Macro is null ? found.Macro : "$(MD_MACROS)\\" + MacroPathBelow(macros, device.Macro);
            if (macro is null)
            {
                violations.Add("'" + device.Tag + "' has no macro from its part: give 'macro'.");
                continue;
            }
            devices.Add(new PlcGenerateDevice { Tag = found.Name, Macro = macro, SingleLinePage = device.SingleLinePage, MultiLinePage = device.MultiLinePage, OverviewPage = device.OverviewPage, RackPage = device.RackPage });
        }
        if (violations.Count > 0)
        { throw WriteArguments.Invalid(violations); }
        return new PlcGenerateRequest(project, devices, [.. targets]);
    }

    private static string MacroPathBelow(IMacroReader macros, string macro)
    {
        string root = macros.MacroRoot() ?? throw new BridgeErrorException(ErrorReason.CapabilityMissing, "EPLAN does not resolve its macro directory, so no macro can be named.");
        _ = MacroFiles.Resolve(root, macro);
        return MacroFiles.Normalized(macro).Replace('/', '\\');
    }

    public string StateFingerprint(IPageReader reader) =>
        Fingerprint.Of(new[] { Project.Path }
            .Concat(Devices.SelectMany(d => new[] { d.Tag, d.Macro, d.SingleLinePage, d.MultiLinePage, d.OverviewPage, d.RackPage }.Select(v => v ?? string.Empty)))
            .Concat(reader.PageNames(Project.Path)));
}

/// <summary><see cref="OperationNames.PlcGeneratePlan"/>: one <c>create</c> per new page, with its device and macro. Reads only.</summary>
public sealed class PlcGeneratePlanOperation(IProjectCatalog catalog, IPageReader reader, IPlcSchematics schematics, IMacroReader macros) : Operation<PlcGenerateArgs, WritePlan>
{
    public override string Name => OperationNames.PlcGeneratePlan;

    protected override WritePlan Execute(PlcGenerateArgs args)
    {
        PlcGenerateRequest request = PlcGenerateRequest.Check(args, catalog, reader, schematics, macros);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = [.. request.Devices.SelectMany(d => PlcGenerateRequest.PagesOf(d).Select(page => new PlannedChange { Action = "create", Kind = "page", Name = page, Property = "plc device", NewValue = d.Tag + " from " + d.Macro }))],
            Fingerprint = request.StateFingerprint(reader),
        };
    }
}

/// <summary>
/// <see cref="OperationNames.PlcGenerateExecute"/>: checks the plan's fingerprint again and lets EPLAN generate the pages in
/// one transaction. EPLAN generating no page rolls back. The pages are read back after the commit.
/// </summary>
public sealed class PlcGenerateExecuteOperation(IProjectCatalog catalog, IPageReader reader, IPlcSchematics schematics, IMacroReader macros)
    : Operation<PlcGenerateArgs, PlcGenerateData>, IWriteOperation
{
    public const string Tool = "eplan_plc_generate";

    public override string Name => OperationNames.PlcGenerateExecute;

    protected override PlcGenerateData Execute(PlcGenerateArgs args)
    {
        PlcGenerateRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            { throw InvalidArgument("'fingerprint' is required: the one plc.generate.plan returned."); }
            request = PlcGenerateRequest.Check(args, catalog, reader, schematics, macros);
            if (!string.Equals(request.StateFingerprint(reader), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(ErrorReason.PlanStale, "The project's pages or PLC macros changed after the plan was made, so nothing was generated. Call eplan_plc_generate again for a new plan.",
                    retryable: false, new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        string path = request.Project.Path;
        Committed<IReadOnlyList<string>> committed = TransactionRunner.Run(schematics.Transactions(path), Tool, () =>
        {
            IReadOnlyList<string> pages = schematics.Generate(path, request.Devices);
            return pages.Count > 0 ? pages : throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN generated no page, so nothing was committed. Check that the macro has the representation asked for (eplan_macro_get).");
        });
        return new PlcGenerateData { Project = request.Project.Name, Pages = [.. schematics.ReadPages(path, committed.Result)], Warning = committed.Warning };
    }
}
