using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>S42/A54: TEXT in SafetyPoint; no property lists or native references survive Abort (A55).</summary>
internal sealed partial class EplanPlacementTextWriter : IPlacementTextWriter
{
    public string? Validate(string projectPath, string page, PlacementText text, bool warn = false)
    {
        MainThread.Assert();
        EplanPlacementStyle.Validate(EplanProjects.ByPath(projectPath), text.Layer, text.Alignment);
        DocumentTypeManager.DocumentType[] allowed = [DocumentTypeManager.DocumentType.Circuit, DocumentTypeManager.DocumentType.CircuitSingleLine, DocumentTypeManager.DocumentType.Overview, DocumentTypeManager.DocumentType.Graphics];
        if (!allowed.Contains(Find(projectPath, page).PageType))
        { throw WriteArguments.Invalid(["/page requires a Circuit, CircuitSingleLine, Overview or Graphics page from eplan_page_list."]); }
        if (text.Kind == "block" && text.Members is { } members)
        {
            Placement[] placements = Find(projectPath, page).AllPlacements;
            if (members.Any(locator => placements.Any(p => p.ObjectIdentifier == locator.Oid && SnapshotCanonical.TypeOf(p.ToStringIdentifier()) == locator.Type && p is SymbolReference)))
            { throw WriteArguments.Invalid(["Block members cannot be functions or symbol references; EPLAN can silently drop functions instead of including them in the block."]); }
        }
        if (text.DimensionType == "circle")
        { Circle(projectPath, page, text.Arc!); }
        if (text.Kind is not ("text" or "path_text" or "hyperlink"))
        { return null; }
        foreach (string language in text.Contents.Keys)
        {
            using var code = new ISOCode(language);
            if (language != "??_??" && (!code.IsValid() || code.GetString() != language))
            { throw WriteArguments.Invalid(["/text/contents requires known EPLAN ISO language codes."]); }
        }
        if (text.Contents.ContainsKey("??_??"))
        { return null; }
        string display;
        try
        { display = new EplanProjectLanguages().Read(projectPath).Display; }
        catch (BridgeErrorException ex) when (text.Kind == "hyperlink" && ex.Error.Reason is ErrorReason.EplanOperationFailed or ErrorReason.CapabilityMissing)
        { return "Project display language is unreadable; hyperlink visibility was not checked."; }
        if (text.Kind == "hyperlink" && string.IsNullOrWhiteSpace(display))
        { return "Project display language is unreadable; hyperlink visibility was not checked."; }
        if (!text.Contents.ContainsKey("??_??") && !LanguageCodes.Split(display).Any(text.Contents.ContainsKey))
        {
            const string message = "/text/contents must include the project display language or ??_?? so the text is visible (A54).";
            if (warn)
            { return message; }
            throw WriteArguments.Invalid([message]);
        }
        return null;
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
        if (text.Kind == "block")
        {
            Page pageObject = Find(projectPath, page);
            Placement[] all = pageObject.AllPlacements;
            Placement[] members = text.Members!.Select(l => all.Single(p => p.ObjectIdentifier == l.Oid && SnapshotCanonical.TypeOf(p.ToStringIdentifier()) == l.Type)).ToArray();
#pragma warning disable CA2000 // Project-owned block; never dispose after rollback (A55).
            var block = new Block();
#pragma warning restore CA2000
            block.Create(pageObject, members);
            if (text.Name is not null)
            { block.Name = text.Name; }
            return () => new() { Kind = "block", Name = block.Name, Count = block.Count, Members = text.Members };
        }
        PointD Point(double[] xy) => new(DrawingCoordinates.MillimetresToApi(xy[0]), DrawingCoordinates.MillimetresToApi(xy[1]));
        double[] XY(PointD point) => [DrawingCoordinates.ApiToMillimetres(point.X), DrawingCoordinates.ApiToMillimetres(point.Y)];
        Func<PlacementText> Styled(GraphicalPlacement placement, Func<PlacementText> read)
        {
            EplanPlacementStyle.Apply(placement, EplanProjects.ByPath(projectPath), text.Layer, text.Pen);
            return () => EplanPlacementStyle.Read(placement, text, read());
        }
        if (AnnotationCreation.Known(text.Kind) && text.Kind != "sector")
        { return Annotation(projectPath, page, text, Styled); }
        if (text.Kind is not ("text" or "path_text"))
        {
#pragma warning disable CA2000 // A39/A54/A55: placements belong to the project, including on rollback.
            if (text.Kind == "polyline")
            {
                var polyline = new PolyLine();
                polyline.Create(Find(projectPath, page));
                for (int i = 0; i < text.Points.Length; i++)
                { PointD point = Point(text.Points[i]); polyline.SetPointAt(i, ref point); }
                polyline.Closed = text.Closed;
                return Styled(polyline, () => new() { Kind = "polyline", Closed = polyline.Closed, Position = XY(polyline.GetPointAt(0)), Points = Enumerable.Range(0, polyline.NumberOfPoints).Select(i => XY(polyline.GetPointAt(i))).ToArray() });
            }
            if (text.Kind is "arc" or "ellipse" or "sector")
            {
                var arc = new Arc();
                arc.Create(Find(projectPath, page));
                bool ellipse = text.Kind == "ellipse", circle = text.Kind == "arc" && DrawingCoordinates.IsCircle(text.StartAngle, text.EndAngle);
                if (ellipse)
                { arc.SetEllipse(Point(text.Position), DrawingCoordinates.MillimetresToApi(text.RadiusXmm), DrawingCoordinates.MillimetresToApi(text.RadiusYmm), text.Angle * (Math.PI / 180)); }
                else if (circle && text.StartAngle != text.EndAngle)
                { arc.SetCircle(Point(text.Position), DrawingCoordinates.MillimetresToApi(text.RadiusMm)); }
                else
                { arc.SetArc(Point(text.Position), DrawingCoordinates.MillimetresToApi(text.RadiusMm), text.StartAngle * (Math.PI / 180), text.EndAngle * (Math.PI / 180), text.Kind == "sector"); }
                return Styled(arc, () =>
                {
                    if (text.Kind == "sector" && !arc.Sector)
                    { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Sector did not read back."); }
                    if ((ellipse || circle) && arc.Kind.ToString() != (ellipse ? "Ellipse" : "Circle"))
                    { throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "Native arc Kind did not read back as the requested Circle or Ellipse."); }
                    return new()
                    {
                        Kind = text.Kind,
                        Position = XY(arc.Location),
                        RadiusMm = DrawingCoordinates.ApiToMillimetres(arc.Radius),
                        StartAngle = arc.StartAngle * (180 / Math.PI),
                        EndAngle = arc.EndAngle * (180 / Math.PI),
                        RadiusXmm = ellipse ? DrawingCoordinates.ApiToMillimetres(arc.FirstRadius) : 0,
                        RadiusYmm = ellipse ? DrawingCoordinates.ApiToMillimetres(arc.SecondRadius) : 0,
                        Angle = ellipse ? arc.ElipseAngle * (180 / Math.PI) : 0
                    };
                });
            }
            if (text.Kind == "image")
            {
                var image = new Image();
                // XML: reference the checked imports file; neither copy nor overwrite project files.
                image.Create(Find(projectPath, page), text.FilePath, Point(text.Position), Point(text.EndPosition), false, false);
                return Styled(image, () => new() { Kind = "image", Position = XY(image.LowerLeftCorner), EndPosition = XY(image.UpperRightCorner), File = text.File, FilePath = PathMap.SubstitutePath(image.FileName, projectPath), Sha256 = text.Sha256 });
            }
            if (text.Kind == "line")
            {
                var line = new Line();
                line.Create(Find(projectPath, page), Point(text.Position), Point(text.EndPosition));
                return Styled(line, () => new() { Kind = text.Kind, Position = XY(line.StartPoint), EndPosition = XY(line.EndPoint) });
            }
            var rectangle = new Rectangle();
#pragma warning restore CA2000
            rectangle.Create(Find(projectPath, page));
            rectangle.SetArea(Point(text.Position), Point(text.EndPosition)); // S42 r3/A54: Create then SetArea.
            rectangle.Angle = Radians(text.Angle);
            return Styled(rectangle, () => new() { Kind = text.Kind, Angle = Degrees(rectangle.Angle), Position = XY(rectangle.LowerLeftCorner), EndPosition = XY(rectangle.UpperRightCorner) });
        }
        using var content = new MultiLangString();
        foreach (KeyValuePair<string, string> entry in text.Contents)
        {
            using var code = new ISOCode(entry.Key);
            content.AddString(entry.Key == "??_??" ? ISOCode.Language.L___ : code.GetNumber(), entry.Value);
        }
#pragma warning disable CA2000 // A39/A55: created placements are project-owned; never dispose them after rollback.
        Text created = text.Kind == "path_text" ? new PathText() : new Text();
#pragma warning restore CA2000
        if (created is PathText pathText)
        { pathText.Create(Find(projectPath, page), content, DrawingCoordinates.MillimetresToApi(text.HeightMm)); }
        else
        { created.Create(Find(projectPath, page), content, DrawingCoordinates.MillimetresToApi(text.HeightMm)); }
        created.Location = new PointD(DrawingCoordinates.MillimetresToApi(text.Position[0]), DrawingCoordinates.MillimetresToApi(text.Position[1]));
        created.Rotation = Radians(text.Angle);
        if (text.Alignment is { } alignment)
        { created.Justification = (TextBase.JustificationType)Enum.Parse(typeof(TextBase.JustificationType), alignment); }
        return Styled(created, () => { PlacementText actual = Read(created); actual.Angle = Degrees(created.Rotation); return actual; });
    }

    private static double Radians(double degrees) => degrees == -16002 ? degrees : degrees * Math.PI / 180;
    private static double Degrees(double radians) => radians == -16002 ? radians : radians * 180 / Math.PI;

    private static PlacementText Read(TextBase created)
    {
        using MultiLangString stored = created.Contents;
        var languages = new LanguageList();
        int count = stored.GetLanguageList(ref languages);
        var result = new PlacementText
        {
            Kind = created is PathText ? "path_text" : "text",
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
        return EplanNativePreconditions.Valid(finder.GetPages(filter)).ToArray().SingleOrDefault(p => string.Equals(p.Name, name, StringComparison.Ordinal))
            ?? throw new BridgeErrorException(ErrorReason.PageNotFound, "Page no longer exists; use eplan_page_list to find its exact name.");
    }
}
