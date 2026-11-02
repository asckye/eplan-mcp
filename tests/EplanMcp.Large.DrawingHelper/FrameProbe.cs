using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Newtonsoft.Json;

namespace EplanMcp.Large.DrawingHelper;

internal static class FrameProbe
{
    private static readonly int[] _frameIds = [12033, 12034, 12003, 12004, 12102, 12002, 12016];
    private static readonly int[] _pageIds = [11016, 10400, 10420, 11012, 11013, 11044, 11048];
    internal static string Read(Project project)
    {
        using var finder = new DMObjectsFinder(project);
        Page[] pages = finder.GetPages(null).Where(p => p.PlotFrame is not null)
            .GroupBy(p => p.PlotFrame.Name, StringComparer.Ordinal).Select(g => g.First()).Take(2).ToArray();
        if (pages.Length != 2)
        { throw new InvalidOperationException("LargeWrite must contain two pages using different plot frames."); }
        object Attempt(Func<object?> read)
        {
            try
            { return read() ?? "<null>"; }
            catch (Exception ex) when (ex is BaseException or InvalidOperationException)
            { return new { error = ex.GetType().Name, ex.Message }; }
        }
        object Value(PropertyValue value)
        {
            using (value)
            { return value.IsEmpty ? "<unset>" : value.ToString(); }
        }
        object Placements(IEnumerable<Placement> placements) => placements.Select(p => new
        {
            kind = p.GetType().Name,
            bounds = Attempt(() => p.GetBoundingBox().Select(q => new[] { q.X, q.Y }).ToArray()),
            location = Attempt(() => new[] { p.Location.X, p.Location.Y }),
        }).ToArray();
        return JsonConvert.SerializeObject(pages.Select(page =>
        {
            var frame = page.PlotFrame;
            using PagePropertyList pageProperties = page.Properties;
            using var frameProperties = frame.Properties;
            var values = new Dictionary<string, object>();
            foreach (int id in _frameIds)
            {
                int captured = id;
                values.Add("frame." + id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Attempt(() => Value(frameProperties[(Properties.PlotFrame)captured])));
                if (id is 12102 or 12002)
                {
                    // Indexed values describe all available grid rows/columns, including unset slots.
                    values.Add("frame.indexed." + id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        Enumerable.Range(1, 1000).Select(i => Attempt(() => Value(frameProperties[(Properties.PlotFrame)captured, i]))).ToArray());
                }
            }
            foreach (int id in _pageIds)
            {
                int captured = id;
                values.Add("page." + id.ToString(System.Globalization.CultureInfo.InvariantCulture), Attempt(() => Value(pageProperties[(Properties.Page)captured])));
            }
            return new
            {
                page = page.Name,
                frame = frame.Name,
                pageSize = Attempt(() => new[] { page.Size.X, page.Size.Y }),
                frameSize = Attempt(() => new[] { frame.Size.X, frame.Size.Y }),
                grid = Attempt(() => page.GridSize),
                pageScale = Attempt(() => Value(pageProperties.PAGE_SCALE)),
                properties = values,
                framePlacements = Placements(frame.SubPlacements),
                pagePlacements = Placements(page.AllPlacements),
            };
        }).ToArray(), Formatting.Indented);
    }
}
