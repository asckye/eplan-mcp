using EplanMcp.Host.Tools;

namespace EplanMcp.Coverage;

internal static partial class ApiTryGenerator
{
    // One input snapshot per composition; the legacy selectors remain unchanged.
    internal sealed class SliceInput
    {
        internal SliceInput(string root)
        {
            Root = root;
            Data = new(() => ApiTryGenerator.Data(root));
            Enums = new(() => ApiTryGenerator.Enums(root));
            Kinds = new(() => ObjectKindGenerator.Kinds(root));
            Services = new(() => ApiTryServices.Load(root));
            Properties = new(() => [.. Selected(Data.Value, Enums.Value, Kinds.Value)]);
            Methods = new(() => [.. ApiTryGenerator.Methods(Data.Value, Enums.Value, Kinds.Value, Services.Value)]);
            Entries = new(() => EntryData(root));
            Handles = new(() => ApiTryGenerator.Handles(root));
        }
        internal string Root { get; }
        internal Lazy<ApiIndexData> Data { get; }
        internal Lazy<Dictionary<string, string[]>> Enums { get; }
        internal Lazy<Dictionary<string, string[]>> Kinds { get; }
        internal Lazy<Dictionary<string, ApiTryServices.Service>> Services { get; }
        internal Lazy<(ApiEntry Entry, ApiDetail Member)[]> Properties { get; }
        internal Lazy<(ApiEntry Entry, ApiDetail Member)[]> Methods { get; }
        internal Lazy<EntryModel> Entries { get; }
        internal Lazy<HandleModel> Handles { get; }
    }
    internal static string Catalog(string root) => ApiTryComposer.Create(root).Render(ApiTrySlot.Catalog);
    internal static string Dispatch(string root) => ApiTryComposer.Create(root).Render(ApiTrySlot.Dispatch);
    internal static string Report(string root) => ApiTryComposer.Create(root).Render(ApiTrySlot.Report);
    internal static string ChainGapReport(string root, bool before = false) => ApiTryComposer.Create(root).Render(before ? ApiTrySlot.ChainGapsBefore : ApiTrySlot.ChainGaps);
    internal static string ChainReachabilityReport(string root) => ApiTryComposer.Create(root).Render(ApiTrySlot.ChainReachability);

    private static ApiTrySelection[] Claims(IEnumerable<(ApiEntry Entry, ApiDetail Member)> members, ApiTryClaim claim = ApiTryClaim.Own) =>
        [.. members.Select(s => new ApiTrySelection(s.Member.Signature ?? s.Member.Id, claim))];
    private static ApiTrySelection[] SettingsOverlap(SliceInput input, IEnumerable<(ApiEntry Entry, ApiDetail Member)> members)
    {
        // Settings also has native via/handle shapes; its project-scoped adapter owns the signature.
        var settings = SettingsMembers(input.Data.Value).Select(s => s.Member.Id).ToHashSet(StringComparer.Ordinal);
        return [.. members.Select(s => new ApiTrySelection(s.Member.Signature ?? s.Member.Id, settings.Contains(s.Member.Id) ? ApiTryClaim.Decorate : ApiTryClaim.Own))];
    }
    private static IEnumerable<(ApiEntry Entry, ApiDetail Member)> AllMembers(SliceInput input) => input.Properties.Value.Concat(input.Methods.Value).Concat(EntrySelected(input.Entries.Value)).Concat(input.Handles.Value.Members);

    internal static ApiTryContribution PropertiesContribution(SliceInput input) => new()
    {
        Members = Claims(input.Properties.Value),
        Templates = { [ApiTrySlot.Catalog] = (c, _) => BaseCatalog(input.Root, c), [ApiTrySlot.Dispatch] = (c, _) => BaseDispatch(input.Root, c), [ApiTrySlot.Report] = (c, _) => BaseReport(input.Root, c) },
    };
    internal static ApiTryContribution MethodsContribution(SliceInput input)
    {
        ApiIndexData data = input.Data.Value;
        var enums = input.Enums.Value;
        var kinds = input.Kinds.Value;
        var services = input.Services.Value;
        var selected = input.Methods.Value;
        return new()
        {
            Members = [.. Claims(selected), .. data.Entries.Where(e => e.Row.Kind == "method" && (ObjectType(e.Type[2..], kinds) || services.ContainsKey(e.Type[2..])))
                .SelectMany(e => e.Details.Where(d => d.Kind == "overload" && !selected.Any(s => s.Member.Id == d.Id))
                    .Select(d => new ApiTrySelection(d.Signature ?? d.Id, ApiTryClaim.Own, Exclusion(e, d, enums, kinds, services) ?? "ambiguous_parameter_names")))],
        };
    }
}
