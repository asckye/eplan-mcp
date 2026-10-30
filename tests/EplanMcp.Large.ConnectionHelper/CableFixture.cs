using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core.Operations;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Large.ConnectionHelper;

/// <summary>Test-only detached locators for the three generated, fictional vertical cable runs (A55).</summary>
internal static class CableFixture
{
    internal static void GenerateAndRead(Project project, Page page)
    {
        using (var safety = SafetyPoint.Create())
        using (var manager = new UndoManager())
        {
            UndoStep undo = manager.CreateUndoStep();
            undo.SetUndoDescription("T8b fixture wire generation");
            using (var quiet = new QuietModeStep(QuietModes.ShowNoDialogs))
            using (var generate = new Generate())
            { generate.Connections([page], false); }
            safety.Commit();
            undo.CloseUndo();
        }
        using var finder = new DMObjectsFinder(project);
        var wires = new List<CableDefinitionPlanner.Wire>();
        foreach (Connection connection in finder.GetConnections(null).Where(c => c.Page?.ObjectIdentifier == page.ObjectIdentifier))
        {
            Pin start = connection.StartPin, end = connection.EndPin;
            Function a = start.ParentFunction, b = end.ParentFunction;
            using ConnectionPropertyList properties = connection.Properties;
            string? Text(int number)
            { using AnyPropertyId id = number; using PropertyValue value = properties[id]; return value.IsEmpty ? null : value.ToString(); }
            var wire = new CableDefinitionPlanner.Wire
            {
                Points = [[a.Location.X + start.Location.X, a.Location.Y + start.Location.Y], [b.Location.X + end.Location.X, b.Location.Y + end.Location.Y]],
                Pins = [FormattableString.Invariant($"{SnapshotCanonical.TypeOf(a.ToStringIdentifier())}/{a.ObjectIdentifier}/{start.Index}"), FormattableString.Invariant($"{SnapshotCanonical.TypeOf(b.ToStringIdentifier())}/{b.ObjectIdentifier}/{end.Index}")],
                Cable = Text(20005),
                Label = Text(31011),
                Colour = Text(31004),
                CrossSection = Text(31002),
            };
            wire.Locator = DrawingLocators.FromIdentifier(project.ProjectFullName, page.Name, connection.ToStringIdentifier(), SnapshotCanonical.Sha256(JObject.FromObject(wire).ToString()));
            wires.Add(wire);
        }
        if (wires.Count != 3 || wires.Any(w => w.Points[0][0] != w.Points[1][0]))
        { throw new InvalidOperationException("Expected three vertical fixture wires."); }
        string file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp", "logs", "large-cable-inventory.json");
        File.WriteAllText(file, JObject.FromObject(new { page = page.Name, wires = wires.OrderBy(w => w.Points[0][0]) }).ToString());
    }
}
