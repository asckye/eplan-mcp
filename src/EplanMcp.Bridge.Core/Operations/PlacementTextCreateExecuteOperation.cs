using System.Globalization;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>T4b internal primitive; unified Host planning, approval and registration wait for T4c.</summary>
public sealed class PlacementTextCreateExecuteOperation(IProjectCatalog catalog, IPlacementTextWriter writer, ITransactionScopeFactory transactions)
    : Operation<PlacementTextCreateArgs, PlacementTextCreateData>, IWriteOperation
{
    public override string Name => OperationNames.PlacementTextCreateExecute;

    public override object ParseArgs(JToken? args)
    {
        void Members(JToken? node, string at, params string[] names)
        {
            if (node is not JObject obj)
            { throw InvalidArgument(at + " must be an object."); }
            foreach (JProperty p in obj.Properties())
            {
                if (!names.Contains(p.Name, StringComparer.Ordinal))
                { throw InvalidArgument(at + "/" + p.Name.Replace("~", "~0").Replace("/", "~1") + " is unknown."); }
            }
            foreach (string name in names)
            { if (obj[name] is null) { throw InvalidArgument(at + "/" + name + " is required."); } }
        }
        Members(args, "", "project", "page", "text");
        Members(args!["text"], "/text", "position", "heightMm", "contents");
        foreach (string name in new[] { "project", "page" })
        { if (args[name]!.Type != JTokenType.String) { throw InvalidArgument("/" + name + " must be a string."); } }
        if (args["text"]!["position"] is not JArray position || position.Count != 2)
        { throw InvalidArgument("/text/position requires [x_mm,y_mm]."); }
        foreach ((JToken number, string at) in position.Select((n, i) => (n, "/text/position/" + i.ToString(CultureInfo.InvariantCulture)))
            .Append((args["text"]!["heightMm"]!, "/text/heightMm")))
        {
            if (number.Type is not (JTokenType.Integer or JTokenType.Float) || !double.TryParse(number.ToString(Newtonsoft.Json.Formatting.None),
                NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || double.IsNaN(value) || double.IsInfinity(value))
            { throw InvalidArgument(at + " must be a finite number."); }
        }
        if (args["text"]!["contents"] is not JObject contents || contents.Properties().Any(p => p.Value.Type != JTokenType.String))
        { throw InvalidArgument("/text/contents requires explicit language strings."); }
        return base.ParseArgs(args);
    }

    protected override PlacementTextCreateData Execute(PlacementTextCreateArgs args)
    {
        OpenProject project;
        PlacementText text = args.Text;
        try
        {
            if (string.IsNullOrWhiteSpace(args.Project) || string.IsNullOrWhiteSpace(args.Page))
            { throw InvalidArgument("/project and /page must be explicit nonempty names."); }
            if (text is null || text.Position is not { Length: 2 } || text.Position.Any(n => double.IsNaN(n) || n is < 0 or > 10000)
                || double.IsNaN(text.HeightMm) || text.HeightMm is < 0.5 or > 100 || text.Contents is null || text.Contents.Count == 0
                || text.Contents.Any(p => (p.Key != "??_??" && (p.Key.Length != 5 || !LanguageCodes.IsCode(p.Key))) || string.IsNullOrWhiteSpace(p.Value))
                || text.Contents.Sum(p => (long)p.Value.Length) > 500)
            { throw InvalidArgument("/text requires two finite 0..10000 mm coordinates, /text/heightMm in 0.5..100 and /text/contents of 1..500 characters with explicit languages."); }
            if (text.Contents.ContainsKey("??_??") && text.Contents.Count > 1)
            { throw InvalidArgument("/text/contents: ??_?? (language-independent) cannot be combined with languages; EPLAN keeps only the languages (live T4b)."); }
            project = WriteArguments.Writable(catalog, args.Project);
            writer.Validate(project.Path, args.Page, text);
        }
        catch (BridgeErrorException ex) { throw WriteArguments.NotStarted(ex); }

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
    }
}
