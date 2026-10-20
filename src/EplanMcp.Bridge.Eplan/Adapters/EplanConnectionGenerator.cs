using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Generates connections on pages the way spikes S9 and S10 did (api-notes A13, A14): <c>Generate.Connections(pages,
/// false)</c>, which updates and never rebuilds all. A page's connections are read as connection.list reads them: all
/// connections once, kept by <c>Connection.Page</c> in memory (A11). Main thread, inside the dispatcher's LockingStep;
/// <see cref="Generate"/> inside the write's TransactionScope. Only the objects this class creates are disposed.
/// </summary>
internal sealed class EplanConnectionGenerator : IConnectionGenerator
{
    private const int MaxMessageLength = 500;

    // Drive-letter and UNC paths, up to the next whitespace or quote.
    private static readonly Regex _paths = new(@"(?:[A-Za-z]:\\|\\\\)[^\s""']+", RegexOptions.CultureInvariant);

    public IReadOnlyList<IReadOnlyList<ConnectionRecord>> ReadConnections(string projectPath, IReadOnlyList<string> pages)
    {
        MainThread.Assert();
        TextLanguage language = EplanText.Language(null);
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        var byPage = pages.Distinct(StringComparer.Ordinal).ToDictionary(page => page, _ => new List<ConnectionRecord>(), StringComparer.Ordinal);
        foreach (Connection connection in finder.GetConnections(null))
        {
            // Page names are unique in a project; the name is what connection.list reports as the connection's page.
            if (EplanConnectionReader.PageOf(connection)?.Name is { } page && byPage.TryGetValue(page, out List<ConnectionRecord>? list))
            {
                list.Add(EplanConnectionReader.Record(connection, language));
            }
        }

        return [.. pages.Select(page => byPage[page])];
    }

    /// <summary>
    /// <c>Page.AllPlacements</c>: every placement on the page, groups resolved (EPLAN XML docs), each by its session
    /// identifier (A3).
    /// </summary>
    public IReadOnlyList<IReadOnlyList<string>> ObjectIds(string projectPath, IReadOnlyList<string> pages)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        return [.. Find(finder, pages).Select(page => (IReadOnlyList<string>)[.. page.AllPlacements.Select(placement => placement.ToStringIdentifier())])];
    }

    /// <summary>
    /// The SafetyPoint locks what the generation touches by itself (S10, A14). EPLAN reports a failed generation as a
    /// BaseException with its message (EPLAN XML docs), which goes back with EPLAN_OPERATION_FAILED; what the exception
    /// mapper knows (locks, a missing licence) stays for it.
    /// </summary>
    public void Generate(string projectPath, IReadOnlyList<string> pages)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        Page[] targets = Find(finder, pages);
        using var generate = new Generate();
        try
        {
            generate.Connections(targets, false);
        }
        catch (BaseException ex) when (new EplanExceptionMapper().Map(ex) is null)
        {
            var failed = new BridgeErrorException(
                ErrorReason.EplanOperationFailed,
                "EPLAN could not generate the connections on these pages, so nothing was changed. metadata.eplanMessage has EPLAN's message; ask the engineer to check the pages in EPLAN.",
                ex);
            failed.Error.Metadata = new Dictionary<string, string> { ["eplanMessage"] = ForModel(ex.Message) };
            throw failed;
        }
    }

    /// <summary>
    /// EPLAN's message as the model may see it (SEC-12): paths replaced, control and format characters dropped, cut at
    /// <see cref="MaxMessageLength"/> without splitting a surrogate pair. The Host cleans it again as project text.
    /// </summary>
    internal static string ForModel(string? message)
    {
        string text = _paths.Replace(message ?? string.Empty, "<path>");
        var kept = new StringBuilder(Math.Min(text.Length, MaxMessageLength + 1));
        foreach (char c in text)
        {
            if (char.IsControl(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.Format)
            {
                if (c is '\r' or '\n' or '\t')
                {
                    kept.Append(' ');
                }

                continue;
            }

            kept.Append(c);
        }

        if (kept.Length <= MaxMessageLength)
        {
            return kept.ToString();
        }

        int cut = char.IsHighSurrogate(kept[MaxMessageLength - 1]) ? MaxMessageLength - 1 : MaxMessageLength;
        return kept.ToString(0, cut) + "…";
    }

    /// <summary>The pages by their exact names, which the operation resolved in the same main-thread call.</summary>
    private static Page[] Find(DMObjectsFinder finder, IReadOnlyList<string> pages) =>
        [.. pages.Select(name => EplanDeviceReader.FindPage(finder, name) ?? throw new InvalidOperationException("A page the operation resolved is gone."))];
}
