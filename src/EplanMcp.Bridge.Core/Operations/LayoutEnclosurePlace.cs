using System.Text.RegularExpressions;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Writes the enclosure (api-notes A136), implemented by the EPLAN adapter; main thread, inside the transaction.</summary>
public interface ILayoutWriter
{
    /// <summary>
    /// Creates the installation space <paramref name="space"/> when the project has none with this designation, places
    /// the enclosure <paramref name="part"/> in it and hangs it below the space; returns the enclosure's object ID.
    /// </summary>
    public long PlaceEnclosure(string projectPath, string space, string part, string variant);
}

/// <summary>The checked arguments of eplan_enclosure_place, shared by its plan and its execution.</summary>
internal sealed class LayoutEnclosurePlaceRequest
{
    private static readonly Regex _designation = new("^[A-Za-z0-9][A-Za-z0-9._]{0,31}$", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    public OpenProject Project { get; private set; } = new();
    public string Space { get; private set; } = string.Empty;
    public string Part { get; private set; } = string.Empty;
    public string Variant { get; private set; } = "1";
    public bool CreatesSpace { get; private set; }
    public string State { get; private set; } = string.Empty;

    public static LayoutEnclosurePlaceRequest Check(LayoutEnclosurePlaceArgs args, IProjectCatalog catalog, ILayoutReader layout, IPartsDatabase parts)
    {
        var violations = new List<string>();
        string space = (args.Space ?? string.Empty).Trim().TrimStart('+');
        if (!_designation.IsMatch(space))
        { violations.Add("'space' must be an installation space designation such as UC1: letters, digits, '.' or '_', at most 32, without '+'."); }
        string part = (args.Part ?? string.Empty).Trim();
        if (part.Length == 0)
        { violations.Add("'part' is required: the enclosure's part number, e.g. RIT.1055500, from eplan_part_search."); }
        if (violations.Count > 0)
        { throw WriteArguments.Invalid(violations); }

        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        string[] numbers = [.. parts.Read(null).Parts.Where(p => string.Equals(p.PartNumber, part, StringComparison.OrdinalIgnoreCase)).Select(p => p.PartNumber).Distinct(StringComparer.Ordinal)];
        if (numbers.Length == 0)
        {
            throw new BridgeErrorException(ErrorReason.PartNotFound, "The parts database has no such part; use eplan_part_search to find the enclosure's part number.", retryable: false,
                new Dictionary<string, string> { ["part"] = part });
        }

        LayoutRecord record = layout.Read(project.Path);
        LayoutSpace? existing = record.Spaces.FirstOrDefault(s => string.Equals(s.Designation, space, StringComparison.Ordinal));
        if (existing is { Enclosures.Count: > 0 })
        {
            throw new BridgeErrorException(ErrorReason.AlreadyExists, "This installation space already has an enclosure; place the next one in another space.", retryable: false,
                new Dictionary<string, string> { ["space"] = space, ["enclosure"] = string.Join(", ", existing.Enclosures.SelectMany(e => e.Parts)) });
        }

        return new LayoutEnclosurePlaceRequest
        {
            Project = project,
            Space = space,
            Part = numbers.Contains(part, StringComparer.Ordinal) ? part : numbers[0],
            Variant = string.IsNullOrWhiteSpace(args.Variant) ? "1" : args.Variant!.Trim(),
            CreatesSpace = existing is null,
            State = Fingerprint.Of(record.Spaces.Select(s => s.Designation + "|" + string.Join(",", s.Enclosures.Select(e => e.Id)))),
        };
    }

    public string StateFingerprint() => Fingerprint.Of([Project.Path, Space, Part, Variant, State]);
}

/// <summary><see cref="OperationNames.LayoutEnclosurePlacePlan"/>: what eplan_enclosure_place would create; reads only.</summary>
public sealed class LayoutEnclosurePlacePlanOperation(IProjectCatalog catalog, ILayoutReader layout, IPartsDatabase parts) : Operation<LayoutEnclosurePlaceArgs, WritePlan>
{
    public override string Name => OperationNames.LayoutEnclosurePlacePlan;

    protected override WritePlan Execute(LayoutEnclosurePlaceArgs args)
    {
        LayoutEnclosurePlaceRequest request = LayoutEnclosurePlaceRequest.Check(args, catalog, layout, parts);
        var changes = new List<PlannedChange>();
        if (request.CreatesSpace)
        { changes.Add(new PlannedChange { Action = "create", Kind = "installation space", Name = request.Space }); }
        changes.Add(new PlannedChange { Action = "create", Kind = "enclosure", Name = request.Space, NewValue = request.Part + " variant " + request.Variant });
        return new WritePlan { Project = request.Project.Name, ProjectPath = request.Project.Path, Changes = changes, Fingerprint = request.StateFingerprint() };
    }
}

/// <summary>
/// <see cref="OperationNames.LayoutEnclosurePlaceExecute"/>: checks again, places the enclosure in one transaction and reads
/// it back: a space with this designation must hold an enclosure with this ID and part and a mounting panel, otherwise the
/// transaction rolls back.
/// </summary>
public sealed class LayoutEnclosurePlaceExecuteOperation(IProjectCatalog catalog, ILayoutReader layout, IPartsDatabase parts, ILayoutWriter writer, ITransactionScopeFactory transactions)
    : Operation<LayoutEnclosurePlaceArgs, LayoutEnclosurePlaceData>, IWriteOperation
{
    /// <summary>The tool whose undo step this is: EPLAN lists it as "AI: eplan_enclosure_place".</summary>
    public const string Tool = "eplan_enclosure_place";

    public override string Name => OperationNames.LayoutEnclosurePlaceExecute;

    protected override LayoutEnclosurePlaceData Execute(LayoutEnclosurePlaceArgs args)
    {
        LayoutEnclosurePlaceRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            { throw InvalidArgument("'fingerprint' is required: the one layout.enclosure.place.plan returned."); }
            request = LayoutEnclosurePlaceRequest.Check(args, catalog, layout, parts);
            if (!string.Equals(request.StateFingerprint(), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(ErrorReason.PlanStale, "The 3D layout changed after the plan was made, so nothing was changed. Call eplan_enclosure_place again for a new plan.",
                    retryable: false, new Dictionary<string, string> { ["space"] = request.Space });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        Committed<LayoutEnclosure> committed = TransactionRunner.Run(transactions, Tool, () =>
        {
            long id = writer.PlaceEnclosure(request.Project.Path, request.Space, request.Part, request.Variant);
            LayoutEnclosure? placed = layout.Read(request.Project.Path).Spaces
                .FirstOrDefault(s => string.Equals(s.Designation, request.Space, StringComparison.Ordinal))?.Enclosures.FirstOrDefault(e => e.Id == id);
            return placed is not null && placed.Parts.Contains(request.Part, StringComparer.OrdinalIgnoreCase) && placed.Panel is not null
                ? placed
                : throw new BridgeErrorException(ErrorReason.EplanOperationFailed,
                    "EPLAN did not place the enclosure with a mounting panel in this installation space, so nothing was kept. The part may have no 3D macro.",
                    retryable: false, new Dictionary<string, string> { ["space"] = request.Space, ["part"] = request.Part });
        });
        return new LayoutEnclosurePlaceData
        {
            Project = request.Project.Name,
            Space = request.Space,
            SpaceCreated = request.CreatesSpace,
            Enclosure = committed.Result,
            Warning = committed.Warning,
        };
    }
}
