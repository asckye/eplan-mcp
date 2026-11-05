namespace EplanMcp.Coverage.Reporting;

internal sealed record ApiBaseline(int Denominator, SortedDictionary<string, RouteCount> Routes);

internal sealed record FullApiMember(string? Kind, string Route, bool LiveVerified);

/// <summary>Counts raw catalog members, including every overload and excluded group, by call route.</summary>
internal sealed record FullApiCoverage(ApiBaseline Total, ApiBaseline Methods, ApiBaseline Properties, ApiBaseline Other)
{
    internal static FullApiCoverage Build(IEnumerable<FullApiMember> members, int undocumentedMethods = 0, int undocumentedProperties = 0)
    {
        FullApiMember[] all = [.. members,
            .. Enumerable.Repeat(new FullApiMember("method", "none", false), undocumentedMethods),
            .. Enumerable.Repeat(new FullApiMember("property", "none", false), undocumentedProperties)];
        static ApiBaseline Count(IEnumerable<FullApiMember> source)
        {
            FullApiMember[] row = [.. source];
            return new(row.Length, CoverageRoutes.Count(row.Select(m => (m.Route, m.LiveVerified))));
        }

        return new(Count(all), Count(all.Where(m => m.Kind == "method")),
            Count(all.Where(m => m.Kind == "property")), Count(all.Where(m => m.Kind is not "method" and not "property")));
    }
}
