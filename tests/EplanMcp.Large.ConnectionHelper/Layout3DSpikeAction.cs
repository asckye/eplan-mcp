using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.E3D;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Eplan.Adapters;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Large.ConnectionHelper;

#pragma warning disable CA2000 // A39: EPLAN owns created placements and pages, including rollback paths.

/// <summary>
/// M4b spike (2026-11-11): what the Pro Panel 3D API does on a scratch copy, step by step, each step recorded on its own so
/// one live run answers every open question of docs research-3d-api: the enclosure's child tree, planes and mate names,
/// rails and ducts snapped onto the mounting panel, how a schematic device reaches the 3D layout, and whether a rolled
/// back transaction leaves nothing behind.
/// </summary>
public sealed class Layout3DSpikeAction : IEplAction
{
    public bool OnRegister(ref string Name, ref int Ordinal) { Name = "EplanMcpLargeLayout3DSpike"; Ordinal = 20; return true; }
    public void GetActionProperties(ref ActionProperties actionProperties) { }

    public bool Execute(ActionCallingContext oActionCallingContext)
    {
        string file = "";
        oActionCallingContext.GetParameter("REQUEST", ref file);
        var result = new JObject { ["success"] = false };
        try
        {
            using var locking = new LockingStep();
            MainThread.Use(System.Windows.Threading.Dispatcher.CurrentDispatcher);
            JObject input = JObject.Parse(File.ReadAllText(file));
            Project project = new SelectionSet().GetCurrentProject(false);
            string home = Environment.GetEnvironmentVariable("EPLANMCP_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "EplanMcp");
            if (!string.Equals(project.ProjectFullName, Path.Combine(home, "scratch", "writes", "LargeWrite"), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(project.ProjectFullName, (string?)input["project"], StringComparison.OrdinalIgnoreCase))
            { throw new InvalidOperationException("Only the current scratch LargeWrite is permitted."); }

            result["before"] = Inventory(project);
            // The sample's own enclosure, as the reference for names and structure.
            using (var finder = new DMObjectsFinder(project))
            {
                Cabinet? reference = finder.GetFunctions3D(null).OfType<Cabinet>().FirstOrDefault();
                result["referenceCabinet"] = reference is null ? null : Tree(reference, 0);
            }
            JArray steps = [];
            result["steps"] = steps;
            if ((string?)input["operation"] == "create")
            {
                bool keep = (bool?)input["keep"] == true;
                using (var scope = new TransactionScope("Layout 3D spike"))
                {
                    Create(project, input, steps);
                    result["during"] = Inventory(project);
                    if (keep)
                    { scope.Commit(); }
                    else
                    { scope.Rollback(); }
                }
                result["after"] = Inventory(project);
            }
            if ((string?)input["operation"] == "modelview")
            {
                InstallationSpace[] spaces = project.InstallationSpaces;
                result["spaces"] = new JArray(spaces.Select(sp => sp.Name + " | " + sp.Properties[36452] + " | " + sp.Properties[36454]));
                foreach (string want in input["spaces"]!.Values<string>().OfType<string>())
                {
                    InstallationSpace? space = spaces.FirstOrDefault(sp => sp.Properties[36452].ToString() == want || sp.Name == want);
                    if (space is null)
                    { steps.Add(new JObject { ["step"] = "model view " + want, ["missing"] = true }); continue; }
                    using var finder = new DMObjectsFinder(project);
                    steps.Add(new JObject { ["step"] = "placed " + want, ["functions"] = new JArray(finder.GetFunctions3D(null).Where(f => f is Cabinet).Take(5).Select(Describe)) });
                    ModelView(project, space, new JObject { ["location"] = "V" + steps.Count }, steps);
                    ModelView(project, space, new JObject { ["location"] = "W" + steps.Count, ["pageType"] = "PanelLayout" }, steps);
                }
            }
            result["success"] = true;
        }
        catch (Exception error)
        {
            result["error"] = error.GetType().FullName + ": " + error.Message;
        }
        File.WriteAllText(file + ".result.json", result.ToString());
        return true;
    }

    private static void Create(Project project, JObject input, JArray steps)
    {
        JObject Step(string name) { var step = new JObject { ["step"] = name }; steps.Add(step); return step; }
        T? Try<T>(JObject step, Func<T> work) where T : class
        {
            try
            { return work(); }
            catch (Exception error)
            { step["error"] = error.GetType().FullName + ": " + error.Message; return null; }
        }

        // Measure candidate enclosures first: each in its own installation space, all rolled back with the rest.
        foreach (JToken candidate in input["measure"] ?? new JArray())
        {
            JObject measure = Step("measure " + (string)candidate!);
            Cabinet? measured = Try(measure, () =>
            {
                var own = new InstallationSpace();
                own.Create(project, "+M" + steps.Count);
                var created = new Cabinet();
                created.Create(own, (string)candidate!, "1");
                return created;
            });
            if (measured is not null)
            {
                measure["box"] = Describe(measured)["box"];
                measure["panel"] = Descendants(measured).OfType<MountingPanel>().Select(Describe).FirstOrDefault();
            }
        }

        JObject spaceStep = Step("installation space");
        InstallationSpace? space = Try(spaceStep, () => { var created = new InstallationSpace(); created.Create(project, (string?)input["space"] ?? "+A9"); return created; });
        if (space is null)
        { return; }
        spaceStep["name"] = space.Name;
        foreach (int id in new[] { 36452, 36453, 36454, 1200, 1100 })
        { spaceStep["p" + id] = Try(spaceStep, () => space.Properties[id].ToString()); }
        if (input["location"] is not null)
        {
            Try(spaceStep, () => { space.Properties[1200] = (string)input["location"]!; return ""; });
            spaceStep["nameAfterLocation"] = space.Name;
        }

        JObject cabinetStep = Step("cabinet");
        Cabinet? cabinet = Try(cabinetStep, () => { var created = new Cabinet(); created.Create(space, (string)input["cabinetPart"]!, (string?)input["cabinetVariant"] ?? "1"); return created; });
        if (cabinet is null)
        { return; }
        cabinetStep["parentAfterCreate"] = cabinet.Parent?.GetType().Name;
        cabinetStep["placedAfterCreate"] = cabinet.IsPlaced;
        if (cabinet.Parent is null)
        {
            Try(cabinetStep, () => { cabinet.SetParent(space, false); return ""; });
            cabinetStep["parentAfterSetParent"] = cabinet.Parent?.GetType().Name;
            cabinetStep["placedAfterSetParent"] = cabinet.IsPlaced;
        }
        cabinetStep["tree"] = Tree(cabinet, 0);

        JObject panelStep = Step("mounting panel");
        MountingPanel? panel = Try(panelStep, () => Descendants(cabinet).OfType<MountingPanel>().FirstOrDefault());
        if (panel is null)
        { panelStep["missing"] = true; return; }
        panelStep["planes"] = new JArray(panel.Planes.Select(p => new JObject { ["name"] = p.Name, ["mode"] = p.PlaneMode.ToString(), ["baseMate"] = p.BaseMate?.Name, ["size"] = p.FieldSize.ToString(), ["offset"] = p.FieldOffset.ToString() }));
        Plane? plane = panel.Planes.FirstOrDefault();
        if (plane is null)
        { return; }

        MountingRail? rail = null;
        JObject railStep = Step("rail");
        rail = Try(railStep, () => { var created = new MountingRail(); created.Create(project, (string)input["railPart"]!, "1", (double?)input["railLength"] ?? 500); return created; });
        if (rail is not null)
        {
            railStep["mates"] = Mates(rail);
            railStep["snapped"] = Snap(rail, plane, (double?)input["railX"] ?? 100, (double?)input["railY"] ?? 600, railStep);
            railStep["placed"] = Describe(rail);
        }

        JObject ductStep = Step("duct");
        Duct? duct = Try(ductStep, () => { var created = new Duct(); created.Create(project, (string)input["ductPart"]!, "1", (double?)input["ductLength"] ?? 500); return created; });
        if (duct is not null)
        {
            ductStep["mates"] = Mates(duct);
            ductStep["snapped"] = Snap(duct, plane, (double?)input["ductX"] ?? 100, (double?)input["ductY"] ?? 700, ductStep);
            ductStep["placed"] = Describe(duct);
        }

        // How a schematic device reaches 3D: an existing unplaced Function3D of the same tag first, else a component by part.
        foreach (JToken device in input["devices"] ?? new JArray())
        {
            string tag = (string)device["tag"]!;
            JObject deviceStep = Step("device " + tag);
            using var finder = new DMObjectsFinder(project);
            Function3D[] existing = [.. finder.GetFunctions3D(null).Where(f => f.Name == tag)];
            deviceStep["existing3d"] = new JArray(existing.Select(Describe));
            Function3D? target = existing.FirstOrDefault(f => !f.IsPlaced);
            if (target is null && device["part"] is not null && rail is not null)
            {
                target = Try(deviceStep, () =>
                {
                    var additional = new List<Placement3D>();
                    return Component.Create(project, rail, (string)device["part"]!, "1", -2, additional);
                });
                if (target is not null)
                {
                    deviceStep["createdName"] = target.Name;
                    Try(deviceStep, () => { target.Name = tag; return ""; });
                }
            }
            if (target is null)
            { continue; }
            deviceStep["mates"] = Mates(target);
            if (rail is not null)
            { deviceStep["snapped"] = SnapToRail(target, rail, (double?)device["x"] ?? 50, deviceStep); }
            deviceStep["placed"] = Describe(target);
            Function[] schematic = [.. finder.GetFunctions(null).Where(f => f.Name == tag)];
            deviceStep["schematicFunctions"] = schematic.Length;
        }
        Door(project, cabinet, input, steps);
        if ((bool?)input["modelView"] == true)
        { ModelView(project, space, input, steps); }
    }

    private static void Door(Project project, Cabinet cabinet, JObject input, JArray steps)
    {
        var flat = new JArray();
        foreach (Placement3D node in Descendants(cabinet))
        {
            JObject row = Describe(node);
            if (node is Plane plane)
            { row["plane"] = plane.Name + " " + plane.PlaneMode + " base=" + plane.BaseMate?.Name; }
            flat.Add(row);
        }
        steps.Add(new JObject { ["step"] = "cabinet flat", ["nodes"] = flat });
        foreach (JToken device in input["doorDevices"] ?? new JArray())
        {
            var step = new JObject { ["step"] = "door device " + (string)device["part"]! };
            steps.Add(step);
            try
            {
                Plane[] planes = [.. Descendants(cabinet).OfType<Plane>().Where(p => p.Parent is not MountingPanel && p.BaseMate is not null)];
                Plane front = planes.OrderBy(p => p.GetBoundingBox(false).Y).First();
                step["plane"] = Describe(front);
                step["planeOwner"] = front.Parent is null ? null : Describe(front.Parent);
                Component component = Component.Create(project, front.Parent!, (string)device["part"]!, "1", -2, []);
                step["mates"] = Mates(component);
                step["snapped"] = Snap(component, front, (double?)device["x"] ?? 100, (double?)device["y"] ?? 100, step);
                step["placed"] = Describe(component);
            }
            catch (Exception error)
            { step["error"] = error.GetType().FullName + ": " + error.Message; }
        }
    }

    private static void ModelView(Project project, InstallationSpace space, JObject input, JArray steps)
    {
        var step = new JObject { ["step"] = "model view" };
        steps.Add(step);
        try
        {
            using var props = new PagePropertyList();
            props.DESIGNATION_LOCATION = (string?)input["location"] ?? "A9";
            props.DESIGNATION_DOCTYPE = "EAA";
            props.PAGE_COUNTER = "1";
            var page = new Page();
            page.Create(project, (string?)input["pageType"] == "PanelLayout" ? DocumentTypeManager.DocumentType.PanelLayout : DocumentTypeManager.DocumentType.ModelView, props);
            step["page"] = page.Name;
            step["pageType"] = page.PageType.ToString();
            Eplan.EplApi.ApplicationFramework.Action? action = new ActionManager().FindAction("InsertModelViewAction");
            step["found"] = action is not null;
            var tries = new JArray();
            step["tries"] = tries;
            string designation = space.Properties[36452].ToString();
            (string Space, string? Structure)[] variants = [(designation, null), (space.Properties[36454].ToString(), null), (designation.TrimStart('+'), null), (space.Name, designation), (designation, designation)];
            foreach ((string layout, string? structure) in variants)
            {
                using var context = new ActionCallingContext();
                context.AddParameter("PROJECTNAME", project.ProjectLinkFilePath);
                context.AddParameter("VIEWNAME", "Spike");
                context.AddParameter("SCALESETTING", "0");
                context.AddParameter("VIEWTYPE", "1");
                context.AddParameter("LAYOUTSPACE", layout);
                if (structure is not null)
                { context.AddParameter("STRUCTURE", structure); }
                context.AddParameter("PAGENAME", page.Name);
                context.AddParameter("X", "40");
                context.AddParameter("Y", "40");
                context.AddParameter("DX", "300");
                context.AddParameter("DY", "220");
                context.AddParameter("VIEWPOINT", "5");
                context.AddParameter("STYLE", "1");
                bool returned = action?.Execute(context) == true;
                string id = "";
                context.GetParameter("OBJECTID", ref id);
                tries.Add(new JObject { ["layout"] = layout, ["structure"] = structure, ["returned"] = returned, ["objectId"] = id, ["placements"] = page.AllPlacements.Length });
                if (returned)
                { break; }
            }
            step["pagePlacements"] = page.AllPlacements.Length;
            step["placementKinds"] = new JArray(page.AllPlacements.Select(p => p.GetType().Name).Distinct());
        }
        catch (Exception error)
        { step["error"] = error.GetType().FullName + ": " + error.Message; }
    }

    private static string? Snap(Placement3D item, Plane plane, double x, double y, JObject step)
    {
        foreach (PointMate source in item.GetSourceMates(Mate.Enums.PlacementOptions.None).OfType<PointMate>())
        {
            try
            {
                if (!source.CanSnapTo(plane.BaseMate))
                { continue; }
                source.SnapTo(plane.BaseMate, 0, x, y);
                return source.Name;
            }
            catch (Exception error)
            { step["snapError " + source.Name] = error.GetType().Name + ": " + error.Message; }
        }
        return null;
    }

    private static string? SnapToRail(Placement3D item, MountingRail rail, double x, JObject step)
    {
        foreach (PointMate source in item.GetSourceMates(Mate.Enums.PlacementOptions.None).OfType<PointMate>())
        {
            try
            {
                if (rail.BaseMate is not LineMate line || !source.CanSnapTo(line))
                { continue; }
                source.SnapTo(line, x);
                return source.Name;
            }
            catch (Exception error)
            { step["snapError " + source.Name] = error.GetType().Name + ": " + error.Message; }
        }
        return null;
    }

    private static IEnumerable<Placement3D> Descendants(Placement3D root)
    {
        foreach (Placement3D child in root.Children)
        {
            yield return child;
            foreach (Placement3D grandchild in Descendants(child))
            { yield return grandchild; }
        }
    }

    private static JObject Tree(Placement3D node, int depth) => new()
    {
        ["node"] = Describe(node),
        ["mates"] = depth < 2 ? Mates(node) : null,
        ["planes"] = new JArray(node.Planes.Select(p => p.Name + " (" + p.PlaneMode + ")")),
        ["children"] = depth < 3 ? new JArray(node.Children.Select(c => Tree(c, depth + 1))) : new JArray(node.Children.Length),
    };

    private static JObject Mates(Placement3D node) => new()
    {
        ["source"] = new JArray(node.GetSourceMates(Mate.Enums.PlacementOptions.None).Select(m => m.GetType().Name + " " + m.Name + " [" + m.MatchingMateNames + "]")),
        ["target"] = new JArray(node.GetTargetMates(false).Select(m => m.GetType().Name + " " + m.Name)),
    };

    private static JObject Describe(Placement3D node)
    {
        var row = new JObject { ["type"] = node.GetType().Name, ["oid"] = node.ObjectIdentifier, ["placed"] = node.IsPlaced, ["parent"] = node.Parent?.GetType().Name };
        if (node is Function3D function)
        { row["name"] = function.Name; row["parts"] = new JArray(function.ArticleReferences.Select(a => a.PartNr)); }
        try
        {
            System.Windows.Media.Media3D.Rect3D box = node.GetBoundingBox(false);
            row["box"] = FormattableString.Invariant($"{box.X:0.#},{box.Y:0.#},{box.Z:0.#} size {box.SizeX:0.#}x{box.SizeY:0.#}x{box.SizeZ:0.#}");
        }
        catch (Exception error)
        { row["boxError"] = error.Message; }
        return row;
    }

    private static JObject Inventory(Project project)
    {
        using var finder = new DMObjectsFinder(project);
        Function3D[] all = [.. finder.GetFunctions3D(null)];
        return new JObject
        {
            ["installationSpaces"] = new JArray(project.InstallationSpaces.Select(s => s.Name)),
            ["functions3d"] = all.Length,
            ["unplaced"] = all.Count(f => !f.IsPlaced),
            ["sample"] = new JArray(all.Take(15).Select(Describe)),
        };
    }
}
