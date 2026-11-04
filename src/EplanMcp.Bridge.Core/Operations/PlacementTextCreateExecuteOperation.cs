using System.Globalization;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A54: mixed primitives share one transaction; the staged single-TEXT request remains supported.</summary>
public sealed class PlacementTextCreateExecuteOperation(IProjectCatalog catalog, IPlacementTextWriter writer, ITransactionScopeFactory transactions, IPageDrawingReader? drawings = null,
    Func<string, string, LayoutGuard.Model>? layoutReader = null, Func<PlacementText, double[]?>? measureText = null, bool planning = false)
    : Operation<PlacementTextCreateArgs, PlacementTextCreateData>, IWriteOperation
{
    private static readonly string[] _imageFields = ["file", "filePath", "sha256"];
    private static readonly string[] _arcFields = ["radiusMm", "startAngle", "endAngle"];
    private static readonly string[] _imageExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".emf", ".wmf"];
    public override string Name => planning ? OperationNames.PlacementTextCreatePlan : OperationNames.PlacementTextCreateExecute;

    public override object ParseArgs(JToken? args)
    {
        void Members(JToken? node, string at, string required, string optional = "")
        {
            string[] names = (required + " " + optional).Split(' ');
            if (node is not JObject obj)
            { throw InvalidArgument(at + " must be an object."); }
            foreach (JProperty p in obj.Properties())
            {
                if (!names.Contains(p.Name, StringComparer.Ordinal))
                { throw InvalidArgument(at + "/" + p.Name.Replace("~", "~0").Replace("/", "~1") + " is unknown."); }
            }
            foreach (string name in required.Split(' ').Where(n => n.Length != 0))
            { if (obj[name] is null) { throw InvalidArgument(at + "/" + name + " is required."); } }
        }
        bool batch = (args as JObject)?["items"] is { Type: not JTokenType.Null };
        Members(args, "", "project page", "text items fingerprint");
        foreach (string name in new[] { "project", "page" })
        { if (args![name]?.Type != JTokenType.String) { throw InvalidArgument("/" + name + " must be a string."); } }
        if (batch && (args!["items"] is not JArray list || list.Count is < 1 or > 50))
        { throw InvalidArgument("/items requires 1..50 primitives."); }
        IEnumerable<JToken> rows = batch ? args!["items"]! : new[] { args!["text"]! };
        int index = 0;
        foreach (JToken row in rows)
        {
            string at = batch ? "/items/" + (index++).ToString(CultureInfo.InvariantCulture) : "/text";
            Members(row, at, "position heightMm contents", "kind endPosition points radiusMm radiusXmm radiusYmm angle startAngle endAngle file filePath sha256 pen layer alignment closed");
            string kind = row["kind"]?.Type == JTokenType.String ? (string)row["kind"]! : "text";
            if (row["kind"] is not null && row["kind"]!.Type != JTokenType.String || !KnownKind(kind))
            { throw InvalidArgument(at + "/kind is not a supported primitive."); }
            foreach (string name in kind is "line" or "rectangle" or "image" ? new[] { "position", "endPosition" } : new[] { "position" })
            {
                if (row[name] is not JArray point || point.Count != 2)
                { throw InvalidArgument(at + "/" + name + " requires [x_mm,y_mm]."); }
                foreach (JToken number in point)
                { Number(number, at + "/" + name); }
            }
            Number(row["heightMm"]!, at + "/heightMm");
            if (row["contents"] is not JObject contents || contents.Properties().Any(p => p.Value.Type != JTokenType.String))
            { throw InvalidArgument(at + "/contents requires explicit language strings."); }
            if (row["angle"] is { Type: not JTokenType.Null } rotation)
            { Number(rotation, at + "/angle"); }
            if (row["layer"] is { Type: not JTokenType.Null } layer && (layer.Type != JTokenType.Integer || !int.TryParse(layer.ToString(), out int id) || id < 0)
                || row["closed"] is { Type: not JTokenType.Null and not JTokenType.Boolean }
                || row["alignment"] is { Type: not JTokenType.Null and not JTokenType.String })
            { throw InvalidArgument(at + ": invalid style type."); }
            if (row["pen"] is { Type: not JTokenType.Null } pen)
            {
                Members(pen, at + "/pen", "", "color width_mm style pattern_length line_end");
                foreach (JProperty value in ((JObject)pen).Properties())
                { Number(value.Value, at + "/pen/" + value.Name); if (!ValidPen(value.Name, (double)value.Value)) { throw InvalidArgument(at + "/pen/" + value.Name + " is out of range."); } }
            }
            if (kind == "polyline" && (row["points"] is not JArray points || points.Count < 2 || points.Any(p => p is not JArray xy || xy.Count != 2)))
            { throw InvalidArgument(at + "/points requires at least two points."); }
            if (kind == "polyline")
            { foreach (JToken n in row["points"]!.SelectMany(p => p.Children())) { Number(n, at + "/points"); } }
            if (kind == "arc")
            { foreach (string name in _arcFields) { Number(row[name], at + "/" + name); } }
            if (kind == "ellipse")
            { foreach (string name in new[] { "radiusXmm", "radiusYmm", "angle" }) { Number(row[name], at + "/" + name); } }
            if (kind == "image" && _imageFields.Any(n => row[n]?.Type != JTokenType.String))
            { throw InvalidArgument(at + "/file requires a resolved import image and digest."); }
        }
        void Number(JToken? number, string at)
        {
            if (number?.Type is not (JTokenType.Integer or JTokenType.Float) || !double.TryParse(number.ToString(Newtonsoft.Json.Formatting.None),
                NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || double.IsNaN(value) || double.IsInfinity(value))
            { throw InvalidArgument(at + " must be a finite number."); }
        }
        return base.ParseArgs(args);
    }

    protected override PlacementTextCreateData Execute(PlacementTextCreateArgs args)
    {
        if (args.Fingerprint is not null && drawings?.Read(WriteArguments.Writable(catalog, args.Project).Path, args.Page)?.Fingerprint() != args.Fingerprint)
        { throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.PlanStale, "Page geometry changed; plan again before creating placements.")); }
        List<PlacementText> items = args.Items ?? [args.Text];
        if (items.Count is < 1 or > 50)
        { throw WriteArguments.NotStarted(InvalidArgument("/items requires 1..50 items.")); }
        var images = new List<ImportSource>();
        foreach (PlacementText item in items)
        {
            if (item is null || !KnownKind(item.Kind) || item.Kind is "line" or "rectangle" or "image" && !Point(item.EndPosition)
                || item.Kind == "polyline" && (item.Points is not { Length: >= 2 } || item.Points.Any(p => !Point(p)))
                || item.Kind == "arc" && (!Finite(item.RadiusMm) || item.RadiusMm <= 0 || !Finite(item.StartAngle) || !Finite(item.EndAngle))
                || item.Kind == "ellipse" && (!Finite(item.RadiusXmm) || item.RadiusXmm <= 0 || !Finite(item.RadiusYmm) || item.RadiusYmm <= 0 || !Finite(item.Angle)))
            { throw WriteArguments.NotStarted(InvalidArgument("/items: invalid kind or endpoint coordinates.")); }
            if (item.Kind == "image")
            {
                var violations = new List<string>();
                ImportSource? image = ImportSource.Check(item.File, item.FilePath, item.Sha256, _imageExtensions, violations);
                if (violations.Count != 0)
                { throw WriteArguments.NotStarted(WriteArguments.Invalid(violations)); }
                try
                { image!.Read(hash: true); images.Add(image); }
                catch (BridgeErrorException ex) { throw WriteArguments.NotStarted(ex); }
            }
            if (!Finite(item.Angle) || item.Layer < 0 || item.Alignment is not null && !IsText(item) || item.Closed && item.Kind != "polyline" || item.Pen?.Any(p => !ValidPen(p.Key, p.Value)) == true)
            { throw WriteArguments.NotStarted(InvalidArgument("/items: invalid style values.")); }
            Validate(new() { Project = args.Project, Page = args.Page, Text = item });
        }
        PageDrawingGeometry geometry = drawings?.Read(WriteArguments.Writable(catalog, args.Project).Path, args.Page)
            ?? throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.CapabilityMissing, "R1/R3: read complete page bounds before creating placements."));
        LayoutGuard.Model layout = layoutReader?.Invoke(geometry.Project, args.Page) ?? new() { Page = geometry };
        layout.SetsTextAttribute = (row, attribute) => attribute is "height_mm" or "position" && row.Kind is "Text" or "PathText" && row.Parent is null
            && items.Any(p => IsText(p) && p.Position.SequenceEqual(row.Location))
            && !geometry.Placements.Any(p => p.Locator.Type == row.Locator.Type && p.Locator.Oid == row.Locator.Oid);
        layout.ReadActual ??= () => new() { Page = drawings!.Read(geometry.Project, args.Page)!, Config = layout.Config, DrawingArea = layout.DrawingArea };
        layout.Lines.AddRange(items.Where(p => p.Kind == "line").Select(p => new LayoutGuard.Segment { Points = [p.Position, p.EndPosition], Changed = true, Net = "graphics" }));
        List<string> warnings = LayoutGuard.Enforce(layout, items.Select((p, i) =>
        {
            double[][] points = p.Kind == "polyline" ? p.Points : [p.Position, p.EndPosition];
            double radius = p.Kind == "ellipse" ? Math.Max(p.RadiusXmm, p.RadiusYmm) : p.RadiusMm;
            double[]? bounds = IsText(p) ? measureText?.Invoke(p) : p.Kind is "arc" or "ellipse"
                ? [p.Position[0] - radius, p.Position[1] - radius, p.Position[0] + radius, p.Position[1] + radius]
                : [points.Min(q => q[0]), points.Min(q => q[1]), points.Max(q => q[0]), points.Max(q => q[1])];
            string kind = p.Kind switch { "path_text" => "PathText", "polyline" => "PolyLine", "ellipse" => "Arc", _ => char.ToUpperInvariant(p.Kind[0]) + p.Kind.Substring(1) };
            DrawingPlacement row = LayoutGuard.Proposed(kind, p.Position, bounds, i);
            row.Properties = new() { ["height_mm"] = p.HeightMm.ToString("R", CultureInfo.InvariantCulture), ["angle"] = "0" };
            return row;
        }).ToArray());
        if (planning)
        { return new() { Text = items[0], Items = items, Warning = string.Join("\n", warnings) }; }
        Committed<List<PlacementText>> batch = TransactionRunner.Run(transactions, "eplan_placement_create", () =>
        {
            foreach (ImportSource image in images)
            { image.Read(hash: true); }
            List<PlacementText> actual = writer.CreateBatch(WriteArguments.Writable(catalog, args.Project).Path, args.Page, items);
            if (actual.Count != items.Count)
            { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Incomplete batch readback; all items were rolled back."); }
            warnings.AddRange(LayoutGuard.AfterWriting(layout));
            return items.Zip(actual, Verify).ToList();
        }, shouldCommit: _ => true);
        return new() { Text = batch.Result[0], Items = batch.Result, Warning = LayoutGuard.Summary(warnings.Append(batch.Warning)) };
    }

    private OpenProject Validate(PlacementTextCreateArgs args)
    {
        OpenProject project;
        PlacementText text = args.Text;
        try
        {
            if (string.IsNullOrWhiteSpace(args.Project) || string.IsNullOrWhiteSpace(args.Page))
            { throw InvalidArgument("/project and /page must be explicit nonempty names."); }
            if (text is null || !Point(text.Position)
                || IsText(text) && ((!Finite(text.HeightMm) || text.HeightMm is < 0.5 or > 100) && !(text.Kind == "path_text" && text.HeightMm == -16002) || text.Contents is null || text.Contents.Count == 0
                || text.Contents.Any(p => (p.Key != "??_??" && (p.Key.Length != 5 || !LanguageCodes.IsCode(p.Key))) || string.IsNullOrWhiteSpace(p.Value))
                || text.Contents.Sum(p => (long)p.Value.Length) > 500))
            { throw InvalidArgument("/text requires finite 0..10000 mm coordinates, heightMm in 0.5..100 (path_text also accepts its default -16002), and 1..500 characters with explicit languages."); }
            if (IsText(text) && text.Contents.ContainsKey("??_??") && text.Contents.Count > 1)
            { throw InvalidArgument("/text/contents: ??_?? (language-independent) cannot be combined with languages; EPLAN keeps only the languages (live T4b)."); }
            project = WriteArguments.Writable(catalog, args.Project);
            writer.Validate(project.Path, args.Page, text);
            return project;
        }
        catch (BridgeErrorException ex) { throw WriteArguments.NotStarted(ex); }
    }

    /// <summary>A rotated rectangle turns about its lower left corner and reads back the turned upper right one.</summary>
    private static double[] ExpectedEnd(PlacementText text)
    {
        if (text.Kind != "rectangle" || text.Angle == 0)
        { return text.EndPosition; }
        double radians = text.Angle * Math.PI / 180, dx = text.EndPosition[0] - text.Position[0], dy = text.EndPosition[1] - text.Position[1];
        return [text.Position[0] + dx * Math.Cos(radians) - dy * Math.Sin(radians), text.Position[1] + dx * Math.Sin(radians) + dy * Math.Cos(radians)];
    }

    /// <summary>EPLAN reads coordinates back with rounding noise; exported sample values have 14 decimals.</summary>
    private static bool Close(double a, double b) => Math.Abs(a - b) <= 1e-6;

    private static bool Close(double[] a, double[] b) => a.Length == b.Length && a.Zip(b, Close).All(close => close);

    private static PlacementText Verify(PlacementText text, PlacementText actual)
    {
        var wrong = new List<string>();
        void Expect(string field, bool same, object? wanted, object? read)
        {
            if (!same)
            { wrong.Add(field + " wanted " + JsonConvert.SerializeObject(wanted) + " read " + JsonConvert.SerializeObject(read)); }
        }
        double[] end = ExpectedEnd(text);
        Expect("kind", actual.Kind == text.Kind, text.Kind, actual.Kind);
        Expect("closed", actual.Closed == text.Closed, text.Closed, actual.Closed);
        Expect("layer", actual.Layer == text.Layer, text.Layer, actual.Layer);
        Expect("alignment", actual.Alignment == text.Alignment, text.Alignment, actual.Alignment);
        Expect("pen", text.Pen is null || actual.Pen is not null && text.Pen.All(p => actual.Pen.TryGetValue(p.Key, out double value) && Close(p.Value, value)), text.Pen, actual.Pen);
        Expect("endPosition", Close(actual.EndPosition, end), end, actual.EndPosition);
        Expect("heightMm", actual.HeightMm == text.HeightMm, text.HeightMm, actual.HeightMm);
        Expect("position", Close(actual.Position, text.Position), text.Position, actual.Position);
        Expect("points", actual.Points.Length == text.Points.Length && !actual.Points.Zip(text.Points, Close).Any(equal => !equal), text.Points, actual.Points);
        Expect("radiusMm", Close(actual.RadiusMm, text.RadiusMm), text.RadiusMm, actual.RadiusMm);
        Expect("angles", Finite(actual.StartAngle) && Finite(actual.EndAngle) && (DrawingCoordinates.IsCircle(text.StartAngle, text.EndAngle)
            || Math.Abs(actual.StartAngle - text.StartAngle) <= 1e-9 && Math.Abs(actual.EndAngle - text.EndAngle) <= 1e-9), new[] { text.StartAngle, text.EndAngle }, new[] { actual.StartAngle, actual.EndAngle });
        Expect("radii", Close(actual.RadiusXmm, text.RadiusXmm) && Close(actual.RadiusYmm, text.RadiusYmm), new[] { text.RadiusXmm, text.RadiusYmm }, new[] { actual.RadiusXmm, actual.RadiusYmm });
        Expect("angle", Finite(actual.Angle) && Math.Abs(actual.Angle - text.Angle) <= 1e-9, text.Angle, actual.Angle);
        Expect("file", actual.File == text.File && (text.Kind != "image" || ProjectResolver.SamePath(actual.FilePath, text.FilePath)), text.File, actual.File);
        Expect("contents", actual.Contents.Count == text.Contents.Count && text.Contents.All(p => actual.Contents.TryGetValue(p.Key, out string? value) && string.Equals(value, p.Value, StringComparison.Ordinal)), text.Contents.Keys, actual.Contents.Keys);
        if (wrong.Count > 0)
        { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Primitive geometry or text languages/height did not read back as requested (" + string.Join("; ", wrong) + "); all changes were rolled back. Ask the engineer to check the page."); }
        actual.FilePath = "";
        actual.Sha256 = "";
        return actual;
    }

    internal static bool ValidPen(string key, double value) => Finite(value) && (key switch
    {
        "color" or "style" => value is >= short.MinValue and <= short.MaxValue && value == Math.Truncate(value),
        "line_end" => value is >= 0 and <= 2 && value == Math.Truncate(value),
        "width_mm" or "pattern_length" => true,
        _ => false
    });
    private static bool KnownKind(string kind) => kind is "text" or "line" or "rectangle" or "polyline" or "arc" or "ellipse" or "path_text" or "image";
    private static bool IsText(PlacementText p) => p.Kind is "text" or "path_text";
    private static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
    private static bool Point(double[]? p) => p is { Length: 2 } && p.All(n => Finite(n) && n is >= 0 and <= 10000);
}
