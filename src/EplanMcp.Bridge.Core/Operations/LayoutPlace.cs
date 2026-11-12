using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>One checked item of eplan_layout_place, as the adapter places it.</summary>
public sealed class LayoutPlaceStep
{
    public int Index { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Part { get; set; } = string.Empty;
    public string Variant { get; set; } = "1";
    public double Length { get; set; }
    public bool Vertical { get; set; }
    public string? Tag { get; set; }
    /// <summary><c>panel</c>, <c>door</c> or <c>rail</c>.</summary>
    public string Surface { get; set; } = "panel";
    /// <summary>The rail's object ID, for a device on a rail placed before this call.</summary>
    public long? RailId { get; set; }
    /// <summary>The index of the rail placed earlier in this call, for a device on it.</summary>
    public int? RailIndex { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Offset { get; set; }
}

/// <summary>Places one item of eplan_layout_place; implemented by the EPLAN adapter, inside the transaction.</summary>
public interface ILayoutPlacer
{
    /// <summary>Places <paramref name="item"/> on the enclosure <paramref name="enclosureId"/>; <paramref name="railId"/> is the resolved rail for a device on a rail.</summary>
    public LayoutPlaced Place(string projectPath, long enclosureId, LayoutPlaceStep item, long? railId);
}

/// <summary>The checked arguments of eplan_layout_place, shared by its plan and its execution.</summary>
internal sealed class LayoutPlaceRequest
{
    public const int MaxItems = 100;

    public OpenProject Project { get; private set; } = new();
    public string Space { get; private set; } = string.Empty;
    public LayoutEnclosure Enclosure { get; private set; } = new();
    public List<LayoutPlaceStep> Steps { get; } = [];
    public string State { get; private set; } = string.Empty;

    public static LayoutPlaceRequest Check(LayoutPlaceArgs args, IProjectCatalog catalog, ILayoutReader layout, IPartsDatabase parts)
    {
        var violations = new List<string>();
        if (args.Items.Count is < 1 or > MaxItems)
        { throw WriteArguments.Invalid([$"'items' must hold 1 to {MaxItems} items."]); }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        string space = (args.Space ?? string.Empty).Trim().TrimStart('+');
        LayoutRecord record = layout.Read(project.Path);
        LayoutEnclosure enclosure = record.Spaces.FirstOrDefault(s => string.Equals(s.Designation, space, StringComparison.Ordinal))?.Enclosures.FirstOrDefault()
            ?? throw new BridgeErrorException(ErrorReason.InvalidArgument, "This installation space has no enclosure; place one with eplan_enclosure_place first.", retryable: false,
                new Dictionary<string, string> { ["space"] = space });
        var known = new HashSet<string>(parts.Read(null).Parts.Select(p => p.PartNumber), StringComparer.OrdinalIgnoreCase);
        var request = new LayoutPlaceRequest { Project = project, Space = space, Enclosure = enclosure };
        var refs = new Dictionary<string, int>(StringComparer.Ordinal);
        var tags = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < args.Items.Count; i++)
        {
            LayoutPlaceItem item = args.Items[i];
            string at = $"items[{i}]";
            string kind = (item.Kind ?? string.Empty).Trim().ToLowerInvariant();
            var step = new LayoutPlaceStep { Index = i, Kind = kind, Variant = string.IsNullOrWhiteSpace(item.Variant) ? "1" : item.Variant!.Trim() };
            string on = (item.On ?? (item.Rail is null ? "panel" : "rail")).Trim().ToLowerInvariant();
            step.Surface = on;
            if (kind is "rail" or "duct")
            {
                step.Part = (item.Part ?? string.Empty).Trim();
                step.Length = item.LengthMm ?? 0;
                step.Vertical = string.Equals(item.Orientation?.Trim(), "vertical", StringComparison.OrdinalIgnoreCase);
                if (step.Part.Length == 0)
                { violations.Add($"{at}: 'part' is required for a {kind}."); }
                if (step.Length is < 10 or > 3000)
                { violations.Add($"{at}: 'length_mm' must be 10 to 3000."); }
                if (item.Orientation is { } o && o.Trim().ToLowerInvariant() is not ("horizontal" or "vertical"))
                { violations.Add($"{at}: 'orientation' must be horizontal or vertical."); }
                if (on is not ("panel" or "door"))
                { violations.Add($"{at}: a {kind} goes on the panel or the door."); }
                if (!string.IsNullOrWhiteSpace(item.Ref))
                {
                    if (refs.ContainsKey(item.Ref!.Trim()))
                    { violations.Add($"{at}: 'ref' {item.Ref} is used twice."); }
                    else
                    { refs[item.Ref!.Trim()] = i; }
                }
            }
            else if (kind == "device")
            {
                string tag = (item.Tag ?? string.Empty).Trim();
                step.Tag = tag;
                LayoutDevice? device = record.Devices.FirstOrDefault(d => string.Equals(d.Tag, tag, StringComparison.Ordinal));
                step.Part = string.IsNullOrWhiteSpace(item.Part) ? device?.Part ?? string.Empty : item.Part!.Trim();
                if (tag.Length == 0)
                { violations.Add($"{at}: 'tag' is required for a device."); }
                else if (device is null && string.IsNullOrWhiteSpace(item.Part))
                { violations.Add($"{at}: no schematic device with a part has the tag {tag}; give 'part' or check eplan_layout_get 'unplaced'."); }
                else if (record.Names3D.Contains(tag) || !tags.Add(tag))
                { violations.Add($"{at}: {tag} already has a 3D placement."); }
                if (on == "rail")
                {
                    string rail = (item.Rail ?? string.Empty).Trim();
                    step.Offset = item.OffsetMm ?? -1;
                    if (refs.TryGetValue(rail, out int index) && request.Steps[index].Kind == "rail")
                    { step.RailIndex = index; }
                    else if (long.TryParse(rail, out long id) && record.Objects.Any(o => o.Id == id && o.Kind == "MountingRail" && o.Space == space))
                    { step.RailId = id; }
                    else
                    { violations.Add($"{at}: 'rail' must name an earlier rail's 'ref' or a placed rail's id."); }
                    if (step.Offset < 0)
                    { violations.Add($"{at}: 'offset_mm' (0 or more) is required on a rail."); }
                }
                else if (on is not ("panel" or "door"))
                { violations.Add($"{at}: 'on' must be panel, door or rail."); }
            }
            else
            { violations.Add($"{at}: 'kind' must be rail, duct or device."); }

            if (on is "panel" or "door")
            {
                LayoutSurface? surface = on == "panel" ? enclosure.Panel : enclosure.Door;
                step.X = item.X ?? -1;
                step.Y = item.Y ?? -1;
                if (surface is null)
                { violations.Add($"{at}: the enclosure has no {on} surface."); }
                else if (step.X < 0 || step.Y < 0 || step.X > surface.Width || step.Y > surface.Height)
                { violations.Add($"{at}: 'x' and 'y' must lie on the {on} (0-{surface.Width} x 0-{surface.Height} mm)."); }
                else if (kind is "rail" or "duct" && (step.Vertical ? step.Y + step.Length : step.X + step.Length) > (step.Vertical ? surface.Height : surface.Width) + 0.5)
                { violations.Add($"{at}: the {kind} runs off the {on}; shorten 'length_mm' or move it."); }
            }

            if (step.Part.Length > 0 && !known.Contains(step.Part))
            { violations.Add($"{at}: the parts database has no part {step.Part}."); }
            request.Steps.Add(step);
        }

        if (violations.Count > 0)
        { throw WriteArguments.Invalid(violations); }
        request.State = Fingerprint.Of(record.Objects.Where(o => o.Space == space).Select(o => o.Id + "|" + string.Join(",", o.Box)));
        return request;
    }

    public string StateFingerprint() => Fingerprint.Of(
        [Project.Path, Space, State, .. Steps.Select(s => string.Join("|", s.Kind, s.Part, s.Variant, s.Length, s.Vertical, s.Tag, s.Surface, s.RailId, s.RailIndex, s.X, s.Y, s.Offset))]);

    /// <summary>Pairs of boxes that share volume, other than an item and its parent or children (a device sits in its rail).</summary>
    internal static List<string> Overlaps(IReadOnlyList<LayoutPlaced> placed, IReadOnlyList<LayoutObject> objects)
    {
        static bool Meet(double[] a, double[] b) => a.Length == 6 && b.Length == 6
            && Enumerable.Range(0, 3).All(k => a[k] < b[k] + b[k + 3] - 1 && b[k] < a[k] + a[k + 3] - 1);
        var parents = objects.ToDictionary(o => o.Id, o => o.ParentId);
        bool Related(long a, long b) => (parents.TryGetValue(a, out long? pa) && pa == b) || (parents.TryGetValue(b, out long? pb) && pb == a);
        var found = new List<string>();
        foreach (LayoutPlaced item in placed)
        {
            foreach (LayoutObject other in objects.Where(o => o.Id != item.Id && o.Kind is "MountingRail" or "Duct" || o.Tag is not null))
            {
                if (other.Id != item.Id && !Related(item.Id, other.Id) && Meet(item.Box, other.Box) && found.Count < 50)
                { found.Add($"items[{item.Index}] ({item.Tag ?? item.Part}) overlaps {other.Kind} {other.Id} ({other.Tag ?? string.Join(",", other.Parts)})"); }
            }
        }
        return found;
    }
}

/// <summary><see cref="OperationNames.LayoutPlacePlan"/>: what eplan_layout_place would create; reads only.</summary>
public sealed class LayoutPlacePlanOperation(IProjectCatalog catalog, ILayoutReader layout, IPartsDatabase parts) : Operation<LayoutPlaceArgs, WritePlan>
{
    public override string Name => OperationNames.LayoutPlacePlan;

    protected override WritePlan Execute(LayoutPlaceArgs args)
    {
        LayoutPlaceRequest request = LayoutPlaceRequest.Check(args, catalog, layout, parts);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = [.. request.Steps.Select(s => new PlannedChange
            {
                Action = "create",
                Kind = s.Kind,
                Name = s.Tag ?? $"items[{s.Index}]",
                NewValue = s.Part + (s.Kind == "device" ? "" : $", {s.Length} mm {(s.Vertical ? "vertical" : "horizontal")}")
                    + (s.Surface == "rail" ? $", on rail at {s.Offset} mm" : $", on {s.Surface} at {s.X},{s.Y} mm"),
            })],
            Fingerprint = request.StateFingerprint(),
        };
    }
}

/// <summary>
/// <see cref="OperationNames.LayoutPlaceExecute"/>: places the items in order in one transaction and reads them back: each
/// must be a placed object of this space, a device under its tag. Overlaps are reported, not rolled back (ADR-0046).
/// </summary>
public sealed class LayoutPlaceExecuteOperation(IProjectCatalog catalog, ILayoutReader layout, IPartsDatabase parts, ILayoutPlacer placer, ITransactionScopeFactory transactions)
    : Operation<LayoutPlaceArgs, LayoutPlaceData>, IWriteOperation
{
    /// <summary>The tool whose undo step this is.</summary>
    public const string Tool = "eplan_layout_place";

    public override string Name => OperationNames.LayoutPlaceExecute;

    protected override LayoutPlaceData Execute(LayoutPlaceArgs args)
    {
        LayoutPlaceRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            { throw InvalidArgument("'fingerprint' is required: the one layout.place.plan returned."); }
            request = LayoutPlaceRequest.Check(args, catalog, layout, parts);
            if (!string.Equals(request.StateFingerprint(), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(ErrorReason.PlanStale, "The 3D layout changed after the plan was made, so nothing was changed. Call eplan_layout_place again for a new plan.",
                    retryable: false, new Dictionary<string, string> { ["space"] = request.Space });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        List<string> overlaps = [];
        Committed<List<LayoutPlaced>> committed = TransactionRunner.Run(transactions, Tool, () =>
        {
            var placed = new List<LayoutPlaced>();
            foreach (LayoutPlaceStep step in request.Steps)
            {
                OperationContext.Current.ThrowIfCancellationRequested();
                long? rail = step.RailIndex is { } index ? placed[index].Id : step.RailId;
                placed.Add(placer.Place(request.Project.Path, request.Enclosure.Id, step, rail));
            }

            LayoutObject[] objects = [.. layout.Read(request.Project.Path).Objects.Where(o => o.Space == request.Space)];
            foreach (LayoutPlaced item in placed)
            {
                LayoutObject? back = objects.FirstOrDefault(o => o.Id == item.Id);
                if (back is null || (item.Tag is not null && !string.Equals(back.Tag, item.Tag, StringComparison.Ordinal)))
                {
                    throw new BridgeErrorException(ErrorReason.EplanOperationFailed,
                        $"items[{item.Index}] did not read back as a placed object of this space{(item.Tag is null ? "" : " named " + item.Tag)}, so nothing was kept.",
                        retryable: false, new Dictionary<string, string> { ["item"] = item.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) });
                }
            }
            overlaps = LayoutPlaceRequest.Overlaps(placed, objects);
            return placed;
        });
        return new LayoutPlaceData { Project = request.Project.Name, Space = request.Space, Items = committed.Result, Overlaps = overlaps, Warning = committed.Warning };
    }
}
