using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// The checked arguments of connection.number.plan and connection.number.execute and what they read; both build them the
/// same way, so they agree on the scheme and the fingerprint (docs/tools/eplan_connection_number.md, section 5). The scope
/// is always the whole project: EPLAN gives wire numbers across it (tool spec 2).
/// </summary>
internal sealed class ConnectionNumberRequest
{
    public const int MaxChanged = 200;

    private ConnectionNumberRequest(OpenProject project, string scheme, string overwrite)
    {
        Project = project;
        Scheme = scheme;
        Overwrite = overwrite;
    }

    public OpenProject Project { get; }

    public string Scheme { get; }

    /// <summary>One of <see cref="ConnectionNumberArgs.OverwriteModes"/>.</summary>
    public string Overwrite { get; }

    /// <summary>Every argument problem at once as INVALID_ARGUMENT (mcp-tool-design 6.2), then the project, then the scheme.</summary>
    public static ConnectionNumberRequest Check(ConnectionNumberArgs args, IProjectCatalog catalog, IConnectionNumberer numberer)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        if (string.IsNullOrWhiteSpace(args.Scheme))
        {
            violations.Add("'scheme' is required: a wire numbering scheme such as Standard.");
        }

        string overwrite = args.Overwrite ?? ConnectionNumberArgs.OverwriteModes[0];
        if (!ConnectionNumberArgs.OverwriteModes.Contains(overwrite))
        {
            violations.Add($"'overwrite' must be one of {string.Join(", ", ConnectionNumberArgs.OverwriteModes)}.");
        }

        if (violations.Count > 0)
        {
            throw WriteArguments.Invalid(violations);
        }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        return new ConnectionNumberRequest(project, SchemeChoice.Require(args.Scheme!.Trim(), numberer.Schemes(project.Path), "wire"), overwrite);
    }

    /// <summary>
    /// The change list's text, e.g. <c>wire numbers in the project (2060 connections, 116 numbered), scheme: Standard,
    /// overwrite: except manual</c>, short enough for the approval dialog's line.
    /// </summary>
    public string Describe(IReadOnlyCollection<DesignatedConnection> connections) =>
        $"wire numbers in the project ({Number(connections.Count)} {(connections.Count == 1 ? "connection" : "connections")}, {Number(Designated(connections))} numbered), "
        + $"scheme: {Scheme}, overwrite: {Overwrite.Replace('_', ' ')}";

    /// <summary>How many of <paramref name="connections"/> have a wire number.</summary>
    public static int Designated(IEnumerable<DesignatedConnection> connections) => connections.Count(c => c.Designation is not null);

    /// <summary>The plan's state: every connection by its session identifier and wire number.</summary>
    public static string FingerprintOf(IEnumerable<DesignatedConnection> connections) => Fingerprint.Of(connections.Select(c => c.Id + "\t" + c.Designation));

    /// <summary>
    /// The connections whose wire number changed, matched by session identifier, in the order of <paramref name="before"/>,
    /// with the page they are on afterwards; and how many connections of <paramref name="before"/> had no identifier left
    /// afterwards, which are counted rather than guessed at.
    /// </summary>
    public static (List<NumberedConnection> Changed, int Unmatched) Changes(IEnumerable<DesignatedConnection> before, IEnumerable<DesignatedConnection> after)
    {
        var now = new Dictionary<string, DesignatedConnection>(StringComparer.Ordinal);
        foreach (DesignatedConnection connection in after)
        {
            now[connection.Id] = connection;
        }

        var changed = new List<NumberedConnection>();
        int unmatched = 0;
        foreach (DesignatedConnection connection in before)
        {
            if (!now.TryGetValue(connection.Id, out DesignatedConnection? then))
            {
                unmatched++;
            }
            else if (!string.Equals(connection.Designation, then.Designation, StringComparison.Ordinal))
            {
                changed.Add(new NumberedConnection { From = connection.Designation, To = then.Designation, Page = then.Page });
            }
        }

        return (changed, unmatched);
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);
}
