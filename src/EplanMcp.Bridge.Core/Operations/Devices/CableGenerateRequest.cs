using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;
using Newtonsoft.Json;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Shared argument, scheme and state checks for cable generation (tool spec 5).</summary>
internal sealed class CableGenerateRequest(OpenProject project, string[] schemes)
{
    public static readonly string[] SchemeKeys = ["creationScheme", "numberingScheme", "selectionScheme"];
    public OpenProject Project { get; } = project;
    public string[] Schemes { get; } = schemes;

    public static CableGenerateRequest Check(CableGenerateArgs args, IProjectCatalog catalog, ICableGenerator generator)
    {
        OperationContext.Current.ThrowIfCancellationRequested();
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        string[] schemes = [args.CreationScheme?.Trim() ?? string.Empty, args.NumberingScheme?.Trim() ?? string.Empty, args.SelectionScheme?.Trim() ?? string.Empty];
        for (int i = 0; i < schemes.Length; i++)
        {
            if (schemes[i].Length == 0)
            {
                violations.Add($"'{SchemeKeys[i]}' is required: an explicit project scheme name.");
            }
        }

        if (violations.Count > 0)
        {
            throw WriteArguments.NotStarted(WriteArguments.Invalid(violations));
        }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        IReadOnlyDictionary<string, IReadOnlyList<string>> offered = generator.Schemes(project.Path);
        for (int i = 0; i < schemes.Length; i++)
        {
            if (!SchemeChoice.Offered(offered[SchemeKeys[i]]).Contains(schemes[i], StringComparer.Ordinal))
            {
                violations.Add($"'{SchemeKeys[i]}' is unknown; choose from metadata.candidates (letter case counts).");
            }
        }

        if (violations.Count > 0)
        {
            BridgeErrorException error = WriteArguments.Invalid(violations);
            error.Error.Metadata!["candidates"] = JsonConvert.SerializeObject(offered.ToDictionary(p => p.Key, p => SchemeChoice.Offered(p.Value).Take(20)));
            throw WriteArguments.NotStarted(error);
        }

        return new CableGenerateRequest(project, schemes);
    }

    public string FingerprintOf(CableGenerationSnapshot snapshot) => Fingerprint.Of([BridgeJson.FromObject(new { Project.Path, Schemes, snapshot.State, Counts = snapshot.Counts(), Cables = snapshot.Cables.OrderBy(c => c.Id, StringComparer.Ordinal) }).ToString(Formatting.None)]);

    public void RequireCurrent(CableGenerationSnapshot snapshot, string? expected)
    {
        OperationContext.Current.ThrowIfCancellationRequested();
        if (!string.Equals(FingerprintOf(snapshot), expected, StringComparison.Ordinal))
        {
            throw WriteArguments.NotStarted(new BridgeErrorException(ErrorReason.PlanStale, "The cable generation scope or schemes changed. Call eplan_cable_generate again for a new plan and approval."));
        }
    }
}
