using Eplan.EplApi.Base;
using Eplan.EplApi.Base.Enums;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.DataModel.Graphics;
using Direction = Eplan.EplApi.DataModel.PinBase.Directions;

namespace EplanMcp.Spikes.S43;

#pragma warning disable CA2000 // Factory results and pins belong to EPLAN (A39).
internal sealed partial class Probe
{
    private static string Tag(string suffix) => "=S43+S43LAB-S43" + suffix;
    private void Name(FunctionBase function, string name)
    {
        TraceStage("name.set", new { requested = name, oid = function.ObjectIdentifier });
        function.Name = name;
        string actual = function.Name;
        report("name_readback", new { requested = name, actual, state = ObjectChanges(function) });
        if (actual != name) { throw new InvalidOperationException("Name was normalized/dropped; requested=" + name + "; actual=" + actual); }
    }
    private T Keep<T>(T reference) where T : SymbolReference { _returned.Add(reference); return reference; }
    private Function Put(Page page, Choice choice, string tag, PointD origin)
    {
        var function = Keep(new Function());
        TraceStage("function.create", new { tag, selected = choice.Info, location = XY(origin) });
        function.Create(page, choice.Variant);
        function.Location = origin;
        Name(function, tag);
        if (function.FunctionCategory != choice.Category || function.ArticleReferences.Length != 0 || !Same(function.Location, origin))
        { throw new InvalidOperationException("Native category/parts/position differs from discovered candidate."); }
        return function;
    }
    private Function Endpoint(Page page, double x, double y, Direction direction, string suffix)
    {
        Choice choice = _selected["two_pole"];
        PointD offset = choice.Pins.Single(p => p.Direction == direction).Location;
        return Put(page, choice, Tag(suffix), new PointD(x - offset.X, y - offset.Y));
    }
    private static void AtPin(SymbolReference reference, Choice choice, PointD target, Direction direction)
    {
        PointD offset = choice.Pins.Single(p => p.Direction == direction).Location;
        reference.Location = new PointD(target.X - offset.X, target.Y - offset.Y);
    }
    private static void Display(FunctionBase parent, int property, PointD location)
    {
        using AnyPropertyId id = property;
        var text = new PropertyPlacement();
        text.Create(parent, id);
        text.Location = location;
        text.IsVisible = true;
        text.Height = 2.5;
        text.Justification = TextBase.JustificationType.BaseLineLeft;
    }
    internal void Place(string group, Action<string, object> log)
    {
        TraceStage("page.prepare", new { group });
        Page page = group == "cable_line" ? RequireGroupPage("cable_wires") : NewPage(Counter(SentinelPage()) + Offset(group), "S43 " + group);
        log("target_page", new { page = page.Name, created = group != "cable_line", grid = page.GridSize, coordinateUnit = "API units", visualVerified = false });
        try
        {
            switch (group)
            {
                case "interruption": Interruptions(page, log); break;
                case "locationbox": CreateLocationBox(page, log); break;
                case "terminals": Terminals(page, log); break;
                case "cable": CreateCable(page); break;
                case "cable_wires": CreateCableRuns(page, group); break;
                case "cable_line": CreateCableDefinition(page, "cable_wires"); break;
                case "plc": Plc(page, log); break;
                case "contacts": Contacts(page, log); break;
                default: throw new ArgumentException("Unknown placement group.");
            }
            // A13: returned objects supplement finder until commit; this read must happen inside Transaction.
            TraceStage("placement.readback", new { group });
            LogSnapshot("inside_transaction", Read());
            log("generate_deferred", new { group = group == "cable_line" ? "cable_wires" : group, reason = "A52: invoke generate only in a separate committed step" });
        }
        catch (Exception ex) { FailureState(ex); throw; }
        finally { _returned.Clear(); }
    }
    private void Interruptions(Page first, Action<string, object> log)
    {
        Choice choice = _selected["interruption"];
        Page peer = NewPage(Counter(first) + 1, "S43 interruption peer");
        foreach ((Page page, int index) in new[] { (first, 1), (peer, 2) })
        {
            SymbolReference created = Keep(SymbolReference.Create(choice.Variant, page));
            if (created is not InterruptionPoint point) { throw new InvalidOperationException("Factory did not return InterruptionPoint."); }
            SetInterruptionName(point, page);
            Direction direction = choice.Pins[0].Direction;
            double g = page.GridSize, x = 20 * g, y = 35 * g;
            AtPin(point, choice, new PointD(x, y), direction);
            double targetY = y + (direction == Direction.Up ? 8 : -8) * g;
            Endpoint(page, x, targetY, direction == Direction.Up ? Direction.Down : Direction.Up, "I" + Evidence.Number(index));
            Display(point, 24300, new PointD(x + 4 * g, y)); // Computed property only; never set reference text/ids.
            log("interruption_requested", new { name = point.Name, page = page.Name, targetPin = XY(new PointD(x, y)), endpoint = XY(new PointD(x, targetY)) });
        }
    }
    private void CreateLocationBox(Page page, Action<string, object> log)
    {
        double g = page.GridSize;
        SymbolReference created = Keep(SymbolReference.Create(_selected["locationbox"].Variant, page));
        if (created is not LocationBox box) { throw new InvalidOperationException("Factory did not return LocationBox; no rectangle substitute."); }
        box.SetLogicalArea(new PointD(10 * g, 20 * g), new PointD(40 * g, 50 * g));
        using (var parts = new FunctionBasePropertyList()) { parts.DESIGNATION_LOCATION = "S43BOX"; box.NameParts = parts; }
        if (Text(box, 1200) != "S43BOX") { throw new InvalidOperationException("Box location structure differs."); }
        Placement? graphics = box.GetGraphics();
        int changed = 0;
        foreach (GraphicalPlacement graphic in Graphics(graphics))
        {
            var pen = graphic.Pen;
            pen.StyleId = 1; // Installed XML: 1 = dash, 0 = continuous.
            graphic.Pen = pen;
            if (graphic.Pen.StyleId != 1) { throw new InvalidOperationException("Dashed pen readback differs."); }
            changed++;
        }
        log("box_pen", new { changed, skipped = changed == 0, reason = changed == 0 ? "No GraphicalPlacement/Pen exposed by GetGraphics" : null });
        // Place after the box. A short visible name lets the native structure mechanism determine the full name.
        var inner = Keep(new Function());
        inner.Create(page, _selected["two_pole"].Variant);
        inner.Location = new PointD(25 * g, 35 * g);
        inner.VisibleName = "-S43L1";
        log("structure_inheritance", new { requestedBox = "+S43BOX", innerName = inner.Name, location = Value(inner, 1200),
            inherited = Text(inner, 1200) == "S43BOX", box = GraphicsData(graphics) });
    }
    private void Terminals(Page page, Action<string, object> log)
    {
        double g = page.GridSize;
        var strip = Keep(new TerminalStrip());
        TraceStage("terminal_strip.create", new { page = page.Name });
        strip.Create(page);
        strip.IsMainFunction = true;
        log("terminal_strip_created", ObjectChanges(strip)!);
        Name(strip, "+S43LAB-S43X");
        for (int i = 1; i <= 3; i++)
        {
            var terminal = Keep(new Terminal());
            Choice choice = _selected["terminal"];
            TraceStage("terminal.create", new { designation = i, stripOid = strip.ObjectIdentifier,
                overload = "Terminal.Create(TerminalStrip, Page, SymbolVariant)" });
            terminal.Create(strip, page, choice.Variant);
            log("terminal_created", new { designation = i, state = ObjectChanges(terminal),
                parentOid = Diagnose(() => terminal.TerminalStrip?.ObjectIdentifier) });
            terminal.Location = new PointD((15 + 15 * i) * g, 35 * g);
            // Name carries both strip identity and terminal designation; never assume 20030 alone establishes membership.
            Name(terminal, strip.Name + ":" + Evidence.Number(i));
            Set(terminal, 20030, Evidence.Number(i));
            TraceStage("terminal.association.read", new { designation = i });
            TerminalStrip? parent = terminal.TerminalStrip;
            string expectedName = strip.Name + ":" + Evidence.Number(i);
            string? actualDesignation = Text(terminal, 20030);
            bool categoryMatches = terminal.FunctionCategory == FunctionCategory.Terminal;
            bool nameMatches = terminal.Name == expectedName;
            bool designationMatches = actualDesignation == Evidence.Number(i);
            bool? parentMatches = parent is null ? null : parent.Name == strip.Name;
            log("terminal_association", new { requestedCategory = "Terminal", actualCategory = terminal.FunctionCategory.ToString(), categoryMatches,
                requestedName = expectedName, actualName = terminal.Name, nameMatches, requestedDesignation = Evidence.Number(i), actualDesignation, designationMatches,
                requestedStrip = strip.Name, actualStrip = parent?.Name, actualStripOid = parent?.ObjectIdentifier, parentMatches,
                relationPendingCommit = parent is null, state = ObjectChanges(terminal) });
            if (!categoryMatches || !nameMatches || !designationMatches || parentMatches == false)
            { throw new InvalidOperationException("Terminal identity/category/designation mismatch; inspect terminal_association comparisons."); }
            // X has two graphical pins and additional internal jumper pins (r2). Only graphical pins define vertical runs.
            foreach (PinBase pin in terminal.GraphicalConnectionPoints)
            {
                if (pin.Direction is not (Direction.Up or Direction.Down)) { throw new InvalidOperationException("Non-vertical terminal pin."); }
                PointD offset = pin.Location;
                Endpoint(page, terminal.Location.X + offset.X, terminal.Location.Y + offset.Y + (pin.Direction == Direction.Up ? 8 : -8) * g,
                    pin.Direction == Direction.Up ? Direction.Down : Direction.Up, "T" + Evidence.Number(i) + pin.Direction);
            }
            Display(terminal, 20030, new PointD(terminal.Location.X + 3 * g, terminal.Location.Y));
        }
    }
    private void CreateCable(Page page)
    {
        CreateCableRuns(page, "cable");
        CreateCableDefinition(page, "cable");
    }
    private void CreateCableRuns(Page page, string group)
    {
        double g = page.GridSize;
        for (int i = 0; i < 3; i++)
        {
            double x = (20 + 15 * i) * g;
            Endpoint(page, x, 45 * g, Direction.Down, RunSuffix(group, true, i + 1));
            Endpoint(page, x, 25 * g, Direction.Up, RunSuffix(group, false, i + 1));
        }
    }
    private void CreateCableDefinition(Page page, string group)
    {
        double g = page.GridSize;
        report("cable_before_definition", CablePageData(page));
        var cable = Keep(new Cable());
        TraceStage("cable.create", new { group, page = page.Name, selected = _selected["cable"].Info });
        cable.Create(page, _selected["cable"].Variant, new PointD(15 * g, 35 * g), new PointD(55 * g, 35 * g));
        Name(cable, CableTag(group));
        Set(cable, 20040, "S43 fictional 3-core");
        Set(cable, 20042, "3");
        Set(cable, 20043, "1.5");
        MatchCableDefinitionNames(page, cable);
        Display(cable, 20214, new PointD(15 * g, 38 * g));
        report("cable_definition_created", CablePageData(page));
    }
    private void Plc(Page page, Action<string, object> log)
    {
        double g = page.GridSize;
        var plc = Keep(new PLC());
        TraceStage("plc.create", new { selected = _selected["plc_box"].Info });
        plc.Create(page, _selected["plc_box"].Variant);
        plc.SetLogicalArea(new PointD(10 * g, 20 * g), new PointD(75 * g, 50 * g));
        Name(plc, Tag("KF"));
        if (plc.FunctionCategory != FunctionCategory.PLCBox) { throw new InvalidOperationException("Expected native PLC box."); }
        Display(plc, 20214, new PointD(12 * g, 48 * g));
        for (int i = 0; i < 3; i++)
        {
            var terminal = Keep(new Terminal());
            Choice choice = _selected["plc_di"];
            TraceStage("plc_terminal.create", new { index = i, parent = ObjectState(plc), selected = choice.Info });
            terminal.Create(plc, choice.Variant);
            log("plc_terminal_created", new { index = i, state = ObjectState(terminal), parent = Diagnose(() => ObjectState(terminal.PLC)) });
            TraceStage("plc_terminal.place", new { index = i });
            terminal.PlaceAt(page, new PointD((20 + 18 * i) * g, 35 * g), DocumentTypeManager.DocumentType.Circuit, choice.Variant);
            if (_plcDiDefinition is not null)
            {
                TraceStage("plc_terminal.definition.set", new { index = i, _plcDiDefinition.Group, _plcDiDefinition.Id, text = DefinitionText(_plcDiDefinition) });
                terminal.FunctionDefinition = _plcDiDefinition;
            }
            Name(terminal, plc.Name + ":" + Evidence.Number(i + 1));
            string[] pinDesignations = Enumerable.Range(1, terminal.Pins.Length).Select(n => terminal.Pins.Length == 1
                ? Evidence.Number(i + 1) : Evidence.Number(i + 1) + "." + Evidence.Number(n)).ToArray();
            if (!terminal.Pins.Select(p => p.Name).SequenceEqual(pinDesignations, StringComparer.Ordinal))
            { SetConnectionDesignations(terminal, pinDesignations, log); }
            // 20030 is not valid for PLCTerminal (r2); channel/plug and logical pin designations have separate fields.
            Set(terminal, PlcFields.Address, "I0." + Evidence.Number(i));
            Set(terminal, PlcFields.SymbolicManual, "S43_DI_" + Evidence.Number(i));
            Set(terminal, PlcFields.Plug, "S43P");
            Set(terminal, PlcFields.Channel, Evidence.Number(i));
            SetText(terminal, "S43 fictional input " + Evidence.Number(i));
            TraceStage("plc_terminal.verify", new { index = i });
            FunctionDefinition? actualDefinition = terminal.FunctionDefinition;
            PLC? parent = terminal.PLC;
            log("plc_terminal_readback", new { index = i, state = ObjectState(terminal), parent = ObjectState(parent),
                definition = actualDefinition is null ? null : DefinitionText(actualDefinition), address = Value(terminal, 20400),
                symbolicAddress = Value(terminal, PlcFields.SymbolicManual), calculatedSymbolicAddress = Value(terminal, PlcFields.SymbolicCalculated) });
            if (terminal.FunctionCategory != FunctionCategory.PLCTerminal || (parent is not null && parent.Name != plc.Name) ||
                actualDefinition is null || !DigitalInput(DefinitionText(actualDefinition)) ||
                terminal.Pins.Length != choice.Pins.Length ||
                (_plcDiDefinition is not null && (actualDefinition.Group != _plcDiDefinition.Group || actualDefinition.Id != _plcDiDefinition.Id)))
            { throw new InvalidOperationException("PLC parent or digital-input function definition differs."); }
            int row = 0;
            foreach (int id in new[] { 20400, 20403, 20011 }) { Display(terminal, id, new PointD(terminal.Location.X, (30 - 3 * row++) * g)); }
        }
        log("plc_display", new { nativePropertyPlacements = true, nativeTableLayoutVerified = false, staticTextUsed = false });
    }
    private void Contacts(Page page, Action<string, object> log)
    {
        double g = page.GridSize;
        foreach ((string role, string suffix, string[] designations, int x) in new[]
        { ("no", "KNO", new[] { "13", "14" }, 20), ("nc", "KNC", new[] { "11", "12" }, 50) })
        {
            Function function = Put(page, _selected[role], Tag(suffix), new PointD(x * g, 35 * g));
            SetConnectionDesignations(function, designations, log);
            Display(function, 20214, new PointD((x + 3) * g, 38 * g));
        }
    }
}
#pragma warning restore CA2000
