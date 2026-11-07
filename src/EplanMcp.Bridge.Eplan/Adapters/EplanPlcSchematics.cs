using System.Xml.Linq;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// PLC schematic generation through <c>PlcService</c> (api-notes A113): the project's own configuration is exported, cut down
/// to the devices asked for and given their pages, as the "Generate PLC schematic" dialog does. Generation creates the pages
/// and joins the functions to the device that is already there, so the macro needs no renaming afterwards.
/// </summary>
internal sealed class EplanPlcSchematics : IPlcSchematics
{
    private const int MaxListed = 200;

    public IReadOnlyList<PlcSchematicDevice> Devices(string projectPath)
    {
        MainThread.Assert();
        return [.. Export(projectPath).Descendants("DEVICE").Select(device => new PlcSchematicDevice
        {
            Name = device.Attribute("Name")?.Value ?? string.Empty,
            Macro = device.Elements("Function").FirstOrDefault(f => f.Attribute("Name")?.Value == device.Attribute("Name")?.Value)?.Attribute("Macro")?.Value,
        })];
    }

    public ITransactionScopeFactory Transactions(string projectPath) => new Factory(projectPath);

    public IReadOnlyList<string> Generate(string projectPath, IReadOnlyList<PlcGenerateDevice> devices)
    {
        MainThread.Assert();
        XDocument configuration = Export(projectPath);
        foreach (XElement device in configuration.Descendants("DEVICE").ToArray())
        {
            string name = device.Attribute("Name")?.Value ?? string.Empty;
            PlcGenerateDevice? wanted = devices.FirstOrDefault(d => d.Tag == name);
            if (wanted is null)
            {
                device.Remove();
                continue;
            }
            foreach (XElement function in device.Elements("Function"))
            {
                function.SetAttributeValue("SingleLinePage", wanted.SingleLinePage);
                function.SetAttributeValue("MultiLinePage", wanted.MultiLinePage);
                function.SetAttributeValue("OverviewPage", wanted.OverviewPage);
                function.SetAttributeValue("RackPage", wanted.RackPage);
                if (function.Attribute("Name")?.Value == name)
                { function.SetAttributeValue("Macro", wanted.Macro); }
            }
        }
        string file = Temporary();
        try
        {
            configuration.Save(file);
            using var service = new PlcService();
            Page[] pages = service.GenerateSchematic(
                EplanProjects.ByPath(projectPath),
                file,
                devices.Any(d => d.SingleLinePage is not null),
                devices.Any(d => d.MultiLinePage is not null),
                devices.Any(d => d.OverviewPage is not null),
                devices.Any(d => d.RackPage is not null));
            return [.. (pages ?? []).Select(p => p.Name)];
        }
        catch (BaseException ex)
        {
            WriteExceptionDiagnostics.Report(ex);
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN could not generate the PLC pages, so nothing was committed: " + ex.Message, ex);
        }
        finally
        {
            File.Delete(file);
        }
    }

    public IReadOnlyList<PlcGeneratedPage> ReadPages(string projectPath, IReadOnlyList<string> pages)
    {
        MainThread.Assert();
        Page[] all = EplanProjects.ByPath(projectPath).Pages;
        return [.. pages.Select(name =>
        {
            Page page = all.Single(p => p.Name == name);
            Function[] functions = [.. EplanNativePreconditions.Valid(page.AllPlacements).OfType<Function>()];
            return new PlcGeneratedPage
            {
                Name = name,
                Type = page.PageType.ToString(),
                Functions = [.. functions.Take(MaxListed).Select(f => f.Name ?? string.Empty)],
                FunctionCount = functions.Length,
            };
        })];
    }

    private static XDocument Export(string projectPath)
    {
        string file = Temporary();
        try
        {
            using var service = new PlcService();
            service.ExportGenerateSchematicConfiguration(EplanProjects.ByPath(projectPath), file);
            return XDocument.Load(file);
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static string Temporary() => Path.Combine(Path.GetTempPath(), "eplanmcp-plc-" + Guid.NewGuid().ToString("N") + ".xml");

    private sealed class Factory(string project) : ITransactionScopeFactory
    { public ITransactionScope Begin(string undoDescription) => new FunctionTransactionScope(EplanProjects.ByPath(project), undoDescription); }
}
