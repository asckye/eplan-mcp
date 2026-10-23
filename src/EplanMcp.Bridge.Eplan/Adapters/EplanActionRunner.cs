using System.Globalization;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using Eplan.EplApi.MasterData;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>S26/A40 and S24/A30 parameters. Main thread under LockingStep; Write caller owns TransactionScope.</summary>
internal sealed class EplanActionRunner : IActionRunner
{
    public ActionRunData Inspect(string entry, string? projectPath, IReadOnlyDictionary<string, string> parameters)
    {
        MainThread.Assert();
        try
        {
            var data = new ActionRunData { Parameters = parameters.Select(p => new ActionParameter { Name = p.Key, Value = p.Value }).ToList() };
            if (projectPath is not null)
            {
                // A39: projects, layers and finder results are borrowed, never disposed. No property lists needed.
                Project project = EplanProjects.ByPath(projectPath);
                data.Target = project.ProjectFullName;
                using var finder = new DMObjectsFinder(project);
                using var cables = new FunctionsFilter { FunctionCategory = FunctionCategory.Cable };
                string[] kinds = ["pages", "functions", "connections", "cables", "terminals", "plc_boxes", "part_references"];
                int[] counts = [finder.GetPages(null).Length, finder.GetFunctions(null).Length, finder.GetConnections(null).Length,
                    finder.GetFunctions(cables).Length, finder.GetTerminals(null).Length, finder.GetPLCs(null).Length, finder.GetArticleReferences(null).Length];
                for (int i = 0; i < kinds.Length; i++) { data.Before["count:" + kinds[i]] = counts[i].ToString(CultureInfo.InvariantCulture); }
                if (entry == "changelayer")
                {
                    var layer = project.LayerTable.Layers.SingleOrDefault(l => l.isValid && !l.isLocked && l.Name == parameters["LAYER1"])
                        ?? throw new BridgeErrorException(ErrorReason.InvalidArgument, "LAYER must name an existing readable unlocked project layer.");
                    data.Before["layer:visible"] = layer.isVisible ? "1" : "0";
                }
                if (entry == "plcservice/BUSDATAEXPORT")
                {
                    using var plc = new PlcService();
                    if (!plc.GetPLCConfigurationProjects(project).Contains(parameters["CONFIGURATIONPROJECT"], StringComparer.Ordinal))
                    { throw new BridgeErrorException(ErrorReason.InvalidArgument, "CONFIGURATIONPROJECT is not in the target project."); }
                }
                Files(project.ProjectFullName + ".edb", string.Empty, data.Before); // A10: ProjectFullName has no .elk, even when the name contains dots.
            }
            else
            {
                bool languages = entry == "backup/MASTERDATA" && parameters["MDTYPE"] == "LANGUAGES";
                string configured = languages ? new EplanMasterDataExporter().TranslationDatabase() : MDPartsManagement.SelectedPartsDatabaseAsString;
                string path = PathMap.SubstitutePath(configured);
                if (!Path.IsPathRooted(path) || path.StartsWith(@"\\", StringComparison.Ordinal) || !File.Exists(path)
                    || !string.Equals(Path.GetExtension(path), languages ? ".db" : ".alk", StringComparison.OrdinalIgnoreCase))
                { throw new BridgeErrorException(ErrorReason.PolicyDenied, "Only the configured local file database is verified for this entry."); }
                data.Target = configured;
                if (entry == "backup/MASTERDATA")
                {
                    data.Parameters.RemoveAll(p => p.Name is "SOURCEPATH" or "FILENAME");
                    data.Parameters.Add(new ActionParameter { Name = "SOURCEPATH", Value = Path.GetDirectoryName(configured) ?? string.Empty });
                    data.Parameters.Add(new ActionParameter { Name = "FILENAME", Value = Path.GetFileName(configured) });
                }
                Files(path, Path.GetFileName(path), data.Before);
                if (languages) { data.Before["count:records"] = "null"; data.Warning = "Translation record count is unavailable. "; }
                else
                {
                    var database = new MDPartsManagement().OpenDatabase();
                    try
                    {
                        string[][] names = [database.Parts.Select(p => p.PartNr).ToArray(), database.Manufacturers.Select(p => p.ShortName).ToArray(),
                            database.Constructions.Select(p => p.Name).ToArray(), database.ConnectionPointInfos.Select(p => p.Name).ToArray(),
                            database.AccessoryLists.Select(p => p.Name).ToArray(), database.AccessoryPlacements.Select(p => p.Name).ToArray()];
                        string[] kinds = ["parts", "manufacturers", "constructions", "connection_patterns", "accessory_lists", "accessory_placements"];
                        string[] ids = ["22001", "22900", "22931", "22945", "22960", "22970"];
                        for (int i = 0; i < kinds.Length; i++)
                        {
                            data.Before["count:" + kinds[i]] = names[i].Length.ToString(CultureInfo.InvariantCulture);
                            if (parameters.TryGetValue("PROPERTYID1", out string? id) && id == ids[i] && !names[i].Contains(parameters["PROPERTYVALUE1"], StringComparer.Ordinal))
                            { throw new BridgeErrorException(ErrorReason.InvalidArgument, "PROPERTYVALUE must be an exact existing record name."); }
                        }
                    }
                    finally { database.Close(); }
                    Files(Path.ChangeExtension(path, ".adb"), "database", data.Before);
                }
            }
            data.Before["target"] = data.Target;
            return data;
        }
        catch (Exception ex) when (ex is not BridgeErrorException && new EplanExceptionMapper().Map(ex) is null)
        { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN could not inspect the action target; check the project or configured database.", ex); }
    }

    public bool Run(string actionName, IReadOnlyDictionary<string, string> parameters)
    {
        MainThread.Assert();
        using var context = new ActionCallingContext();
        foreach (KeyValuePair<string, string> parameter in parameters) { context.AddParameter(parameter.Key, parameter.Value); }
        using var quiet = new QuietModeStep(QuietModes.ShowNoDialogs);
#pragma warning disable RS0030 // ADR-0021: only shipped whitelist entries, rechecked by Bridge.Core, into the Host's folder or Write transaction.
        var action = new ActionManager().FindAction(actionName) ?? throw new InvalidOperationException("EPLAN has no action for this whitelist entry.");
#pragma warning restore RS0030
        return action.Execute(context);
    }

    private static void Files(string path, string relative, Dictionary<string, string> state)
    {
        FileAttributes attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0) { throw new IOException("Fingerprint contains a link."); }
        if ((attributes & FileAttributes.Directory) != 0)
        {
            foreach (string child in Directory.EnumerateFileSystemEntries(path).OrderBy(p => p, StringComparer.Ordinal))
            { Files(child, relative.Length == 0 ? Path.GetFileName(child) : relative + "/" + Path.GetFileName(child), state); }
        }
        else if (!path.EndsWith(".lck", StringComparison.OrdinalIgnoreCase) && !string.Equals(Path.GetFileName(path), "User.eod", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Path.GetFileName(path), "User.eox", StringComparison.OrdinalIgnoreCase))
        {
            var file = new FileInfo(path);
            state["file:" + relative] = file.Length.ToString(CultureInfo.InvariantCulture) + "\t" + file.LastWriteTimeUtc.ToString("O", CultureInfo.InvariantCulture);
        }
    }
}
