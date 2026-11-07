using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>The checked arguments of eplan_project_update and the stored values they would change (ADR-0052).</summary>
internal sealed class ProjectUpdateRequest
{
    private const int MaxLength = 500;
    private const int MaxProperties = 50;

    private ProjectUpdateRequest(OpenProject project, List<(string Key, string Before, string After)> all)
    {
        Project = project;
        All = all;
    }

    public OpenProject Project { get; }

    /// <summary>Every property of the call with its stored value now.</summary>
    public List<(string Key, string Before, string After)> All { get; }

    /// <summary>The properties whose value differs.</summary>
    public IEnumerable<(string Key, string Before, string After)> Changing => All.Where(p => !TextLanguages.SameValue(p.Before, p.After));

    public static ProjectUpdateRequest Check(ProjectUpdateArgs args, IProjectCatalog catalog, IProjectProperties properties, ITextTranslator translator)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        List<ProjectPropertyValue> given = args.Properties ?? [];
        if (given.Count < 1 || given.Count > MaxProperties)
        { violations.Add(FormattableString.Invariant($"'properties' must hold 1 to {MaxProperties} entries.")); }
        var seen = new HashSet<string>(StringComparer.Ordinal);
        bool denied = false;
        for (int i = 0; i < given.Count; i++)
        {
            ProjectPropertyValue item = given[i];
            string at = FormattableString.Invariant($"/properties/{i}");
            bool named = item.Id == 0 && item.Name is { } userDefined && userDefined.StartsWith(ProjectUpdateArgs.UserDefinedPrefix, StringComparison.Ordinal);
            if (!named && !ProjectUpdateArgs.TitleBlock.ContainsKey(item.Id))
            {
                denied = true;
                violations.Add(at + "/id: only the title block properties and the template's own " + ProjectUpdateArgs.UserDefinedPrefix + " properties may be set: " + string.Join(", ", ProjectUpdateArgs.TitleBlock.Keys) + ".");
                continue;
            }
            if (!seen.Add(Key(item)))
            { violations.Add(at + "/id: give each property once."); }
            bool multilingual = named || ProjectUpdateArgs.TitleBlock[item.Id];
            if (item.Value is not { } value || value.Length > MaxLength || value.Any(char.IsControl))
            { violations.Add(at + "/value: supply text of at most 500 characters without controls; empty clears it."); }
            else if (!multilingual && TextLanguages.IsSerialized(value))
            { violations.Add(at + "/value: this property holds one plain text, not one per language."); }
        }
        if (denied)
        {
            throw new BridgeErrorException(ErrorReason.PolicyDenied, "This project property may not be set. " + string.Join(" ", violations), retryable: false);
        }
        if (violations.Count > 0)
        { throw WriteArguments.Invalid(violations); }
        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        string Value(ProjectPropertyValue p) => args.Translate && p.Value is { Length: > 0 } text && !TextLanguages.IsSerialized(text)
            && (p.Id == 0 || ProjectUpdateArgs.TitleBlock[p.Id]) ? translator.Translate(project.Path, text) : p.Value!;
        return new ProjectUpdateRequest(project, [.. given.Select(p => (Key(p), properties.Read(project.Path, Key(p)), Value(p)))]);
    }

    private static string Key(ProjectPropertyValue p) => p.Id == 0 ? p.Name ?? string.Empty : p.Id.ToString(CultureInfo.InvariantCulture);

    public string StateFingerprint() => Fingerprint.Of(new[] { Project.Path }.Concat(All.SelectMany(p => new[] { p.Key, p.Before, p.After })));
}
