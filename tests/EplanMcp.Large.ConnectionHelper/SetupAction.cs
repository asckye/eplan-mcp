using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using Eplan.EplApi.HEServices;

namespace EplanMcp.Large.ConnectionHelper;

/// <summary>Test-only S40 fixture functions on an existing empty Circuit page of the current LargeWrite copy.</summary>
public sealed class SetupAction : IEplAction
{
    public bool OnRegister(ref string Name, ref int Ordinal) { Name = "EplanMcpLargeConnectionSetup"; Ordinal = 20; return true; }
    public void GetActionProperties(ref ActionProperties actionProperties) { }
    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        // Remote action callbacks run on EPLAN's main thread (S35/S40); contain every exception at the boundary.
        try
        {
            using var locking = new LockingStep();
            string pageName = "", directions = "";
            oActionCallingContext.GetParameter("PAGE", ref pageName);
            oActionCallingContext.GetParameter("DIRECTIONS", ref directions);
            Project project = new SelectionSet().GetCurrentProject(false);
            string expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp", "scratch", "writes", "LargeWrite");
            if (!string.Equals(project.ProjectFullName, expected, StringComparison.OrdinalIgnoreCase) || !pageName.StartsWith("=GAA+A1&EFS1/", StringComparison.Ordinal))
            { Log("project/page: " + project.ProjectFullName + " | " + pageName); return false; }
            Page page = project.Pages.Single(p => p.Name == pageName);
            if (page.PageType.ToString() != "Circuit" || page.AllPlacements.Length != 0)
            { Log("page: " + page.PageType + " placements=" + page.AllPlacements.Length); return false; }
            double grid = page.GridSize, x = grid * 30, y = grid * 40;
            var choices = new List<(SymbolVariant Variant, PointD Position)>();
            foreach (string direction in directions.Split(','))
            {
                string opposite = direction switch { "Up" => "Down", "Down" => "Up", "Left" => "Right", "Right" => "Left", _ => throw new InvalidOperationException("Invalid fixture direction.") };
                double dx = direction == "Right" ? 8 * grid : direction == "Left" ? -8 * grid : 0;
                double dy = direction == "Up" ? 8 * grid : direction == "Down" ? -8 * grid : 0;
                var choice = project.SymbolLibraries.Where(l => l.Name == "IEC_symbol").SelectMany(l => l.Symbols)
                    .Where(s => s.Type.ToString() == "Function").SelectMany(s => s.Variants)
                    .Select(v => (Variant: v, Pins: v.ConnectionPoints))
                    .Where(v => v.Pins.Length == 2 && v.Pins.Any(p => p.Direction.ToString() == opposite)
                        && v.Pins.All(p => Math.Abs(p.Location.X) <= 2 * grid && Math.Abs(p.Location.Y) <= 2 * grid
                            && Math.Abs(p.Location.X / grid - Math.Round(p.Location.X / grid)) < 1e-8 && Math.Abs(p.Location.Y / grid - Math.Round(p.Location.Y / grid)) < 1e-8))
                    .OrderBy(v => v.Variant.SymbolName, StringComparer.Ordinal).ThenBy(v => v.Variant.VariantNr).First();
                PinBase pin = choice.Pins.First(p => p.Direction.ToString() == opposite);
                choices.Add((choice.Variant, new PointD(x + dx - pin.Location.X, y + dy - pin.Location.Y)));
            }
            // A13/A52: Function.Create requires UndoStep -> Transaction, with discovery outside the write.
            project.LockAllObjects();
            using var manager = new UndoManager();
            UndoStep undo = manager.CreateUndoStep();
            undo.SetUndoDescription("T3b fixture setup");
            using Transaction transaction = new TransactionManager().CreateTransaction();
            try
            {
                using (var quiet = new QuietModeStep(QuietModes.ShowNoDialogs))
                {
                    int index = 0;
                    foreach (var choice in choices)
                    {
#pragma warning disable CA2000 // A39/S40: native storable objects, including newly created functions, are borrowed.
                        var function = new Function();
#pragma warning restore CA2000
                        function.Create(page, choice.Variant);
                        function.Location = choice.Position;
                        function.Name = FormattableString.Invariant($"-T3B{page.ObjectIdentifier}{++index}");
                        if (function.ArticleReferences.Length != 0)
                        { throw new InvalidOperationException("Unexpected fixture parts."); }
                    }
                }
                transaction.Commit();
                undo.CloseUndo();
            }
            catch { transaction.Abort(); throw; }
            return true;
        }
        catch (Exception ex) { Log(ex.ToString()); return false; }
    }

    private static void Log(string message) => File.AppendAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp", "logs", "large-connection-helper.log"), DateTime.Now.ToString("s") + " " + message + Environment.NewLine);
}
