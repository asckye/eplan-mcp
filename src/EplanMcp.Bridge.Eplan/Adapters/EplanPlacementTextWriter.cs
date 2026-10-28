using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>S42/A54: TEXT in SafetyPoint; no property lists or native references survive Abort (A55).</summary>
internal sealed class EplanPlacementTextWriter : IPlacementTextWriter
{
    public void Validate(string projectPath, string page, PlacementText text)
    {
        MainThread.Assert();
        if (Find(projectPath, page).PageType != DocumentTypeManager.DocumentType.Circuit)
        { throw WriteArguments.Invalid(["/page must be an exact Circuit page from eplan_page_list."]); }
        if (text.Kind != "text")
        { return; }
        foreach (string language in text.Contents.Keys)
        {
            using var code = new ISOCode(language);
            if (language != "??_??" && (!code.IsValid() || code.GetString() != language))
            { throw WriteArguments.Invalid(["/text/contents requires known EPLAN ISO language codes."]); }
        }
        if (!text.Contents.ContainsKey("??_??") && !LanguageCodes.Split(new EplanProjectLanguages().Read(projectPath).Display).Any(text.Contents.ContainsKey))
        { throw WriteArguments.Invalid(["/text/contents must include the project display language or ??_?? so the text is visible (A54)."]); }
    }

    public List<PlacementText> CreateBatch(string projectPath, string page, IReadOnlyList<PlacementText> items)
    {
        Func<PlacementText>[] reads = items.Select(item => Place(projectPath, page, item)).ToArray();
        return reads.Select(read => read()).ToList();
    }

    public PlacementText Create(string projectPath, string page, PlacementText text) => Place(projectPath, page, text)();

    private static Func<PlacementText> Place(string projectPath, string page, PlacementText text)
    {
        MainThread.Assert();
        OperationContext.Current.ThrowIfCancellationRequested();
        PointD Point(double[] xy) => new(DrawingCoordinates.MillimetresToApi(xy[0]), DrawingCoordinates.MillimetresToApi(xy[1]));
        double[] XY(PointD point) => [DrawingCoordinates.ApiToMillimetres(point.X), DrawingCoordinates.ApiToMillimetres(point.Y)];
        if (text.Kind != "text")
        {
#pragma warning disable CA2000 // A39/A54/A55: placements belong to the project, including on rollback.
            if (text.Kind == "line")
            {
                var line = new Line();
                line.Create(Find(projectPath, page), Point(text.Position), Point(text.EndPosition));
                return () => new() { Kind = text.Kind, Position = XY(line.StartPoint), EndPosition = XY(line.EndPoint) };
            }
            var rectangle = new Rectangle();
#pragma warning restore CA2000
            rectangle.Create(Find(projectPath, page));
            rectangle.SetArea(Point(text.Position), Point(text.EndPosition)); // S42 r3/A54: Create then SetArea.
            return () => new() { Kind = text.Kind, Position = XY(rectangle.LowerLeftCorner), EndPosition = XY(rectangle.UpperRightCorner) };
        }
        using var content = new MultiLangString();
        foreach (KeyValuePair<string, string> entry in text.Contents)
        {
            using var code = new ISOCode(entry.Key);
            content.AddString(entry.Key == "??_??" ? ISOCode.Language.L___ : code.GetNumber(), entry.Value);
        }
#pragma warning disable CA2000 // A39/A55: created placements are project-owned; never dispose them after rollback.
        var created = new Text();
#pragma warning restore CA2000
        created.Create(Find(projectPath, page), content, DrawingCoordinates.MillimetresToApi(text.HeightMm));
        created.Location = new PointD(DrawingCoordinates.MillimetresToApi(text.Position[0]), DrawingCoordinates.MillimetresToApi(text.Position[1]));
        return () => Read(created);
    }

    private static PlacementText Read(Text created)
    {
        using MultiLangString stored = created.Contents;
        var languages = new LanguageList();
        int count = stored.GetLanguageList(ref languages);
        var result = new PlacementText
        {
            HeightMm = DrawingCoordinates.ApiToMillimetres(created.Height),
            Position = [DrawingCoordinates.ApiToMillimetres(created.Location.X), DrawingCoordinates.ApiToMillimetres(created.Location.Y)]
        };
        for (int i = 0; i < count; i++)
        {
            ISOCode.Language language = languages.get_Language(i);
            using var code = new ISOCode(language);
            result.Contents.Add(code.GetString(), stored.GetString(language));
        }
        return result;
    }

    private static Page Find(string projectPath, string name)
    {
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        using var filter = new PagesFilter { Name = name, ExactNameMatching = true };
        return finder.GetPages(filter).SingleOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal))
            ?? throw new BridgeErrorException(ErrorReason.PageNotFound, "Page no longer exists; use eplan_page_list to find its exact name.");
    }
}
