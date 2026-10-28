using System.Globalization;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>A54: mixed primitives share one transaction; the staged single-TEXT request remains supported.</summary>
public sealed class PlacementTextCreateExecuteOperation(IProjectCatalog catalog, IPlacementTextWriter writer, ITransactionScopeFactory transactions, IPageDrawingReader? drawings = null)
    : Operation<PlacementTextCreateArgs, PlacementTextCreateData>, IWriteOperation
{
    public override string Name => OperationNames.PlacementTextCreateExecute;

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
            foreach (string name in required.Split(' '))
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
            Members(row, at, "position heightMm contents", "kind endPosition");
            string kind = row["kind"]?.Type == JTokenType.String ? (string)row["kind"]! : "text";
            if (row["kind"] is not null && row["kind"]!.Type != JTokenType.String || kind is not ("text" or "line" or "rectangle"))
            { throw InvalidArgument(at + "/kind requires text, line or rectangle."); }
            foreach (string name in kind == "text" ? new[] { "position" } : new[] { "position", "endPosition" })
            {
                if (row[name] is not JArray point || point.Count != 2)
                { throw InvalidArgument(at + "/" + name + " requires [x_mm,y_mm]."); }
                foreach (JToken number in point)
                { Number(number, at + "/" + name); }
            }
            Number(row["heightMm"]!, at + "/heightMm");
            if (row["contents"] is not JObject contents || contents.Properties().Any(p => p.Value.Type != JTokenType.String))
            { throw InvalidArgument(at + "/contents requires explicit language strings."); }
        }
        void Number(JToken number, string at)
        {
            if (number.Type is not (JTokenType.Integer or JTokenType.Float) || !double.TryParse(number.ToString(Newtonsoft.Json.Formatting.None),
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
        foreach (PlacementText item in items)
        {
            if (item is null || item.Kind is not ("text" or "line" or "rectangle") || item.Kind != "text" && (item.EndPosition is not { Length: 2 }
                || item.EndPosition.Any(n => double.IsNaN(n) || n is < 0 or > 10000)))
            { throw WriteArguments.NotStarted(InvalidArgument("/items: invalid kind or endpoint coordinates.")); }
            Validate(new() { Project = args.Project, Page = args.Page, Text = item });
        }
        Committed<List<PlacementText>> batch = TransactionRunner.Run(transactions, "eplan_placement_create", () =>
        {
            List<PlacementText> actual = writer.CreateBatch(WriteArguments.Writable(catalog, args.Project).Path, args.Page, items);
            if (actual.Count != items.Count)
            { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Incomplete batch readback; all items were rolled back."); }
            return items.Zip(actual, Verify).ToList();
        }, shouldCommit: _ => true);
        return new() { Text = batch.Result[0], Items = batch.Result, Warning = batch.Warning };
    }

    private OpenProject Validate(PlacementTextCreateArgs args)
    {
        OpenProject project;
        PlacementText text = args.Text;
        try
        {
            if (string.IsNullOrWhiteSpace(args.Project) || string.IsNullOrWhiteSpace(args.Page))
            { throw InvalidArgument("/project and /page must be explicit nonempty names."); }
            if (text is null || text.Position is not { Length: 2 } || text.Position.Any(n => double.IsNaN(n) || n is < 0 or > 10000)
                || text.Kind == "text" && (double.IsNaN(text.HeightMm) || text.HeightMm is < 0.5 or > 100 || text.Contents is null || text.Contents.Count == 0
                || text.Contents.Any(p => (p.Key != "??_??" && (p.Key.Length != 5 || !LanguageCodes.IsCode(p.Key))) || string.IsNullOrWhiteSpace(p.Value))
                || text.Contents.Sum(p => (long)p.Value.Length) > 500))
            { throw InvalidArgument("/text requires two finite 0..10000 mm coordinates, /text/heightMm in 0.5..100 and /text/contents of 1..500 characters with explicit languages."); }
            if (text.Kind == "text" && text.Contents.ContainsKey("??_??") && text.Contents.Count > 1)
            { throw InvalidArgument("/text/contents: ??_?? (language-independent) cannot be combined with languages; EPLAN keeps only the languages (live T4b)."); }
            project = WriteArguments.Writable(catalog, args.Project);
            writer.Validate(project.Path, args.Page, text);
            return project;
        }
        catch (BridgeErrorException ex) { throw WriteArguments.NotStarted(ex); }
    }

<<<<<<< HEAD
        Committed<PlacementText> committed = TransactionRunner.Run(transactions, "eplan_placement_create", () =>
        {
            PlacementText actual = writer.Create(project.Path, args.Page, text);
            if (actual.HeightMm != text.HeightMm || !actual.Position.SequenceEqual(text.Position) || actual.Contents.Count != text.Contents.Count
                || text.Contents.Any(p => !actual.Contents.TryGetValue(p.Key, out string? value) || !string.Equals(value, p.Value, StringComparison.Ordinal)))
            {
                throw new BridgeErrorException(ErrorReason.EplanOperationFailed, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                    "Text languages, height or position did not read back as requested (read {0} mm at {1}, languages {2}); all changes were rolled back. Ask the engineer to check the page.",
                    actual.HeightMm, string.Join(",", actual.Position), string.Join(",", actual.Contents.Keys)));
            }
            return actual;
        }, shouldCommit: _ => true); // A54: rollback without closing the failed creation's undo step.
        return new PlacementTextCreateData { Text = committed.Result, Warning = committed.Warning };
=======
    private static PlacementText Verify(PlacementText text, PlacementText actual)
    {
        if (actual.Kind != text.Kind || !actual.EndPosition.SequenceEqual(text.EndPosition) || actual.HeightMm != text.HeightMm || !actual.Position.SequenceEqual(text.Position) || actual.Contents.Count != text.Contents.Count
            || text.Contents.Any(p => !actual.Contents.TryGetValue(p.Key, out string? value) || !string.Equals(value, p.Value, StringComparison.Ordinal)))
        { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Primitive geometry or text languages/height did not read back as requested; all changes were rolled back. Ask the engineer to check the page."); }
        return actual;
>>>>>>> b4fd63bf (feat(tools): create texts, lines and boxes with eplan_placement_create)
    }
}
