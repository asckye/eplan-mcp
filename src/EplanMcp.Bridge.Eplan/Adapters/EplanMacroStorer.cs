using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using Eplan.EplApi.DataModel.MasterData;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>WindowMacro.Create and PageMacro.Create, verified on EPLAN 2026.0.3 (2026-11-05).</summary>
internal sealed class EplanMacroStorer : IMacroStorer
{
    public bool KnowsLanguage(string language) => new EplanPageWriter().KnowsLanguage(language);
    public Func<int[]> Prepare(string projectPath, JObject args)
    {
        MainThread.Assert();
        Project project = EplanProjects.ByPath(projectPath);
        using var finder = new DMObjectsFinder(project);
        Page Page(string name) => EplanNativePreconditions.Valid(finder.GetPages(null)).SingleOrDefault(p => p.Name == name)
            ?? throw new BridgeErrorException(ErrorReason.PageNotFound, "Select an exact page name from eplan_page_list.");
        bool window = (string?)args["operation"] == "window";
        Page[] pages = window ? [Page((string)args["page"]!)] : args["pages"]!.Values<string>().Select(n => Page(n!)).ToArray();
        Placement[] selected = [];
        if (window)
        {
            var placements = new Dictionary<string, Placement>(StringComparer.Ordinal);
            void Visit(Placement placement)
            {
                if (placements.ContainsKey(placement.ToStringIdentifier()))
                { return; }
                placements.Add(placement.ToStringIdentifier(), placement);
                if (placement.Group is { } group)
                { Visit(group); }
                if (placement is Group parent)
                { foreach (Placement child in parent.SubPlacements) { Visit(child); } }
            }
            foreach (Placement placement in EplanNativePreconditions.Valid(pages[0].AllPlacements))
            { Visit(placement); }
            Placement[] available = placements.Values.ToArray();
            selected = args["locators"]!.Select(token =>
            {
                DrawingLocator locator = token.ToObject<DrawingLocator>()!;
                if (!ProjectResolver.SamePath(locator.Project, projectPath) || locator.Page != pages[0].Name)
                { throw WriteArguments.Invalid(["All locators must belong to the selected project and page."]); }
                Placement item = available.SingleOrDefault(p => p.ObjectIdentifier == locator.Oid && SnapshotCanonical.TypeOf(p.ToStringIdentifier()) == locator.Type)
                    ?? throw new BridgeErrorException(ErrorReason.PlanStale, "A macro placement disappeared; call eplan_object_find again.");
                if (item is PropertyPlacement || available.OfType<Block>().Any(b => b.SubPlacements.Any(p => p.ObjectIdentifier == item.ObjectIdentifier && p.ToStringIdentifier() == item.ToStringIdentifier())))
                { throw WriteArguments.Invalid(["Property text and block members accompany their parent and cannot be selected separately."]); }
                string kind = ObjectKindDispatch.Kind(item) ?? item.GetType().Name.ToLowerInvariant();
                string digest = SnapshotCanonical.Sha256(JsonConvert.SerializeObject(EplanObjectReader.Describe(item, kind)));
                if (digest != locator.Digest)
                { throw new BridgeErrorException(ErrorReason.PlanStale, "A macro locator changed; call eplan_object_find again."); }
                return item;
            }).ToArray();
            if (args["reference_mm"] is null)
            {
                PointD[] bounds = selected.SelectMany(p => p.GetBoundingBox()).ToArray();
                args["reference_mm"] = new JArray(bounds.Min(p => p.X), bounds.Min(p => p.Y));
            }
        }
        return () =>
        {
            using var description = new MultiLangString();
            if (args["description"] is JObject texts)
            { foreach (JProperty entry in texts.Properties()) { description.AddString(EplanText.Language(entry.Name).Number, (string)entry.Value!); } }
            else if (args["description"]?.Type == JTokenType.String)
            { description.AddString(EplanText.Language(null).Number, (string)args["description"]!); }
            string path = (string)args["path"]!;
            if (!window)
            {
                using var pageMacro = new PageMacro();
                pageMacro.Create(path, pages, description);
                return [];
            }
            var representation = (WindowMacro.Enums.RepresentationType)Enum.Parse(typeof(WindowMacro.Enums.RepresentationType), (string)args["representation"]!);
            using var macro = new WindowMacro();
            macro.Create(path, representation, (int)args["variant"]!, selected, description, new PointD((double)args["reference_mm"]![0]!, (double)args["reference_mm"]![1]!));
            macro.Open(path, project);
            return macro.GetVariants(representation);
        };
    }
}
