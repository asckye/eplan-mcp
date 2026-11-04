using System.Globalization;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.MasterData;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Eplan.Adapters;

internal sealed partial class EplanConnectionCreator
{
    private readonly Dictionary<int, long> _definitionPointIds = [];
    private static readonly (string Name, int Id)[] _definitionProperties = [("color", 31004), ("cross_section", 31002), ("designation", 31011)];
    private static readonly (string Name, int Id)[] _definitionSources = [("function_definition", 20188), ("representation_type", 20121), ("type_code", 31048)];
    private static SymbolVariant DefinitionVariant(string project, JToken row) => EplanProjects.ByPath(project).SymbolLibraries
        .Single(l => l.Name == "SPECIAL").Symbols.Single(s => s.Name == (string)row["symbol"]!["name"]!).Variants
        .Single(v => v.VariantNr == (int)row["symbol"]!["variant"]!);

    public Action PrepareDefinitionPoints(string project, string page, JArray planned)
    {
        AssertCurrent(project);
        _ = Page(project, page);
        foreach (JToken row in planned)
        { if ((string?)row["type"] == "ConnectionDefinitionPoint") { _ = DefinitionVariant(project, row); } }
        _definitionPointIds.Clear();
        return () =>
        {
            Page target = Page(project, page);
            foreach (JToken row in planned)
            {
                OperationContext.Current.ThrowIfCancellationRequested();
                if ((string?)row["type"] == "PotentialDefinition")
                { _definitionPointIds.Add((int)row["itemIndex"]!, InsertPotential(target, row).ObjectIdentifier); continue; }
#pragma warning disable CA2000 // A39/A86: newly created native objects remain project-owned, including on Abort.
                var point = new Function();
#pragma warning restore CA2000
                try
                {
                    point.Create(target, DefinitionVariant(project, row));
                    point.Location = new PointD((double)row["position"]![0]!, (double)row["position"]![1]!);
                    using UniversalPropertyList properties = point.Properties;
                    foreach (var property in _definitionProperties)
                    {
                        if (row["properties"]![property.Name] is not { } value)
                        { continue; }
                        using AnyPropertyId id = property.Id;
                        properties[id] = (string)value!;
                    }
                    _definitionPointIds.Add((int)row["itemIndex"]!, point.ObjectIdentifier);
                }
                finally { GC.KeepAlive(point); }
            }
        };
    }

    public void AddDefinitionArticles(string project, string page, JArray planned)
    {
        JToken[] rows = [.. planned.Where(r => r["articles"] is JArray)];
        if (rows.Length == 0)
        { return; }
        Placement[] placements = EplanNativePreconditions.Valid(Page(project, page).AllPlacements).ToArray();
        using Transaction transaction = new TransactionManager().CreateTransaction();
        foreach (JToken row in rows)
        {
            long oid = _definitionPointIds[(int)row["itemIndex"]!];
            ConnectionDefinitionPoint point = placements.OfType<ConnectionDefinitionPoint>().Single(p => p.ObjectIdentifier == oid);
            foreach (JToken article in (JArray)row["articles"]!)
            { point.AddArticleReference((string)article["part"]!, (string)article["variant"]!, (uint)article["count"]!); }
        }
        transaction.Commit();
    }

    public void WriteDefinitionSources(string project, string page, JArray planned)
    {
        foreach (JToken row in planned.Where(r => (string?)r["type"] == "ConnectionDefinitionPoint" && ((JObject)r["definition"]!).HasValues))
        {
            long oid = _definitionPointIds[(int)row["itemIndex"]!];
            using Transaction transaction = new TransactionManager().CreateTransaction();
            ConnectionDefinitionPoint point = EplanNativePreconditions.Valid(Page(project, page).AllPlacements).OfType<ConnectionDefinitionPoint>().Single(p => p.ObjectIdentifier == oid);
            using var finder = new DMObjectsFinder(EplanProjects.ByPath(project));
            Connection owner = EplanNativePreconditions.Valid(finder.GetConnections(null))
                .Single(c => EplanConnectionReader.PageOf(c)?.Name == page && c.ConnectionDefPoints.Any(p => p.ObjectIdentifier == oid));
            using UniversalPropertyList properties = point.Properties;
            foreach (var property in _definitionSources)
            {
                if (row["definition"]![property.Name] is not { } value)
                { continue; }
                using AnyPropertyId id = property.Id;
                if (property.Name == "representation_type")
                { properties[id] = int.Parse((string)value!, CultureInfo.InvariantCulture); }
                else
                { properties[id] = (string)value!; }
                // A93: EPLAN does not pass a point's change on to its existing connection.
                if (property.Name == "type_code")
                {
                    using UniversalPropertyList connection = owner.Properties;
                    connection[id] = (string)value!;
                }
            }
            transaction.Commit();
            GC.KeepAlive(point);
            GC.KeepAlive(owner);
        }
    }

    public JArray ReadDefinitionPoints(string project, string page, JArray planned)
    {
        Placement[] placements = EplanNativePreconditions.Valid(Page(project, page).AllPlacements).ToArray();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(project));
        Connection[] connections = EplanNativePreconditions.Valid(finder.GetConnections(null)).Where(c => EplanConnectionReader.PageOf(c)?.Name == page).ToArray();
        PageDrawingGeometry geometry = new EplanDrawingReader().Read(project, page)!;
        var wires = new Dictionary<long, CableDefinitionPlanner.Wire>();
        var result = new JArray();
        foreach (JToken expected in planned)
        {
            long oid = _definitionPointIds[(int)expected["itemIndex"]!];
            if ((string?)expected["type"] == "PotentialDefinition")
            { result.Add(ReadPotential(placements.OfType<PotentialDefinition>().Single(p => p.ObjectIdentifier == oid), expected, geometry)); continue; }
            ConnectionDefinitionPoint? point = placements.OfType<ConnectionDefinitionPoint>().SingleOrDefault(p => p.ObjectIdentifier == oid);
            if (point is null)
            { throw DefinitionMismatch(FormattableString.Invariant($"item {expected["itemIndex"]}: created point {oid} is missing")); }
            Connection[] owners = connections.Where(c => c.ConnectionDefPoints.Any(p => p.ObjectIdentifier == oid)).ToArray();
            if (owners.Length != 1)
            { throw DefinitionMismatch(FormattableString.Invariant($"item {expected["itemIndex"]}: connection membership count is {owners.Length}")); }
            try
            {
                if (!wires.TryGetValue(owners[0].ObjectIdentifier, out CableDefinitionPlanner.Wire? connection))
                { wires.Add(owners[0].ObjectIdentifier, connection = ReadCableWire(project, page, owners[0])); }
                JObject values, sources;
                // One list per wrapper: a second Properties after this release reads freed memory (A93).
                using (UniversalPropertyList properties = point.Properties)
                {
                    values = ReadDefinitionProperties(properties);
                    sources = ReadDefinitionSources(properties, (JObject)expected["definition"]!);
                }
                var row = new JObject
                {
                    ["itemIndex"] = expected["itemIndex"]!.DeepClone(),
                    ["type"] = point.GetType().Name,
                    ["position"] = new JArray(XY(point.Location)),
                    ["endpoints"] = JArray.FromObject(CablePins(owners[0])),
                    ["symbol"] = new JObject { ["library"] = point.SymbolVariant.SymbolLibraryName, ["name"] = point.SymbolVariant.SymbolName, ["variant"] = point.SymbolVariant.VariantNr },
                    ["properties"] = values,
                    ["definition"] = sources,
                    ["connectionValues"] = new JObject { ["color"] = connection.Colour, ["cross_section"] = connection.CrossSection, ["designation"] = connection.Label },
                    ["connection"] = BridgeJson.FromObject(connection.Locator),
                };
                if (expected["articles"] is JArray)
                {
                    static JObject Article(ArticleReference a) => new() { ["part"] = a.PartNr, ["variant"] = a.VariantNr, ["count"] = a.Count };
                    row["articles"] = new JArray(point.ArticleReferences.Select(Article));
                    row["connectionArticles"] = new JArray(owners[0].ArticleReferences.Select(Article));
                }
                row["locator"] = BridgeJson.FromObject(geometry.Placements.Single(p => p.Kind == nameof(ConnectionDefinitionPoint) && p.Locator.Oid == oid).Locator);
                result.Add(row);
            }
            finally { GC.KeepAlive(point); GC.KeepAlive(owners[0]); }
        }
        return result;
    }
    private static JObject ReadDefinitionSources(UniversalPropertyList properties, JObject requested)
    {
        var result = new JObject();
        foreach (var property in _definitionSources.Where(s => requested.ContainsKey(s.Name)))
        {
            using AnyPropertyId id = property.Id;
            using PropertyValue value = properties[id];
            result[property.Name] = value.ToString();
        }
        return result;
    }

    private static JObject ReadDefinitionProperties(UniversalPropertyList properties)
    {
        var result = new JObject();
        foreach (var property in _definitionProperties)
        {
            using AnyPropertyId id = property.Id;
            using PropertyValue value = properties[id];
            result[property.Name] = EplanText.Read(value, EplanText.Language(null));
        }
        return result;
    }
    private static BridgeErrorException DefinitionMismatch(string detail) => new(ErrorReason.EplanOperationFailed, "Definition point readback differs: " + detail + ".");
}
