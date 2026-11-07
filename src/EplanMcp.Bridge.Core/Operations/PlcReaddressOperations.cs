using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Reads the addresses of a project's PLC connection points and lets EPLAN assign them, implemented by the EPLAN adapter layer. <see cref="Readdress"/> only inside the write's transaction.</summary>
public interface IPlcAddressing
{
    public IReadOnlyList<PlcAddressEntry> Read(string projectPath);

    public void Readdress(string projectPath, PlcReaddressArgs args);
}

/// <summary>The checked arguments of eplan_plc_readdress.</summary>
internal sealed class PlcReaddressRequest
{
    public const int MaxChanged = 200;

    private PlcReaddressRequest(OpenProject project, IReadOnlyList<PlcAddressEntry> points)
    {
        Project = project;
        Points = points;
    }

    public OpenProject Project { get; }

    public IReadOnlyList<PlcAddressEntry> Points { get; }

    public static PlcReaddressRequest Check(PlcReaddressArgs args, IProjectCatalog catalog, IPlcAddressing addressing)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        foreach ((string name, string? value) in new[] { ("digital_input", args.DigitalInput), ("digital_output", args.DigitalOutput), ("analog_input", args.AnalogInput), ("analog_output", args.AnalogOutput), ("cpu", args.Cpu) })
        {
            if (value is { } text && (text.Length > 100 || text.Any(char.IsControl)))
            { violations.Add("'" + name + "' must be text of at most 100 characters without controls."); }
        }
        if (args.Sorting is not (null or "connection_point" or "channel" or "graphical"))
        { violations.Add("'sorting' must be connection_point, channel or graphical."); }
        if (violations.Count > 0)
        { throw WriteArguments.Invalid(violations); }
        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        return new PlcReaddressRequest(project, addressing.Read(project.Path));
    }

    public string StateFingerprint(PlcReaddressArgs args) => Fingerprint.Of(new[] { Project.Path, args.DigitalInput ?? string.Empty, args.DigitalOutput ?? string.Empty, args.AnalogInput ?? string.Empty, args.AnalogOutput ?? string.Empty, args.Cpu ?? string.Empty, args.Sorting ?? string.Empty }
        .Concat(Points.Select(p => p.Id + "=" + p.Address)));

    public string Scope() => FormattableString.Invariant($"PLC addresses of up to {Points.Count} connection points; only EPLAN knows the new ones");
}

/// <summary><see cref="OperationNames.PlcReaddressPlan"/>: the scope, since only EPLAN can tell the new addresses. Reads only.</summary>
public sealed class PlcReaddressPlanOperation(IProjectCatalog catalog, IPlcAddressing addressing) : Operation<PlcReaddressArgs, WritePlan>
{
    public override string Name => OperationNames.PlcReaddressPlan;

    protected override WritePlan Execute(PlcReaddressArgs args)
    {
        PlcReaddressRequest request = PlcReaddressRequest.Check(args, catalog, addressing);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = request.Points.Count == 0 ? [] : [new PlannedChange { Action = "update", Kind = "plc", Name = request.Project.Name, Property = "numbering", OldValue = request.Scope() }],
            Fingerprint = request.StateFingerprint(args),
        };
    }
}

/// <summary>
/// <see cref="OperationNames.PlcReaddressExecute"/>: checks the plan's fingerprint again, backs the project up
/// (<see cref="ProjectBackups"/>), lets EPLAN assign the addresses in one transaction and reads back what changed.
/// EPLAN changing nothing is still committed, with no change listed.
/// </summary>
public sealed class PlcReaddressExecuteOperation(IProjectCatalog catalog, IPlcAddressing addressing, IPageReader pages, IProjectBackup backup, ITransactionScopeFactory transactions)
    : Operation<PlcReaddressArgs, PlcReaddressData>, IWriteOperation
{
    public const string Tool = "eplan_plc_readdress";

    public override string Name => OperationNames.PlcReaddressExecute;

    protected override PlcReaddressData Execute(PlcReaddressArgs args)
    {
        PlcReaddressRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            { throw InvalidArgument("'fingerprint' is required: the one plc.readdress.plan returned."); }
            request = PlcReaddressRequest.Check(args, catalog, addressing);
            if (!string.Equals(request.StateFingerprint(args), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(ErrorReason.PlanStale, "The PLC addresses changed after the plan was made, so nothing was addressed. Call eplan_plc_readdress again for a new plan.",
                    retryable: false, new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        var data = new PlcReaddressData { Project = request.Project.Name };
        if (request.Points.Count == 0)
        { return data; }
        string path = request.Project.Path;
        data.Backup = ProjectBackups.Make(backup, pages, request.Project, args.Backup);
        Committed<List<PlcAddressChange>> committed = TransactionRunner.Run(transactions, Tool, () =>
        {
            addressing.Readdress(path, args);
            Dictionary<string, string> before = request.Points.GroupBy(p => p.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Address, StringComparer.Ordinal);
            return addressing.Read(path)
                .Where(p => before.TryGetValue(p.Id, out string? old) && !string.Equals(old, p.Address, StringComparison.Ordinal))
                .Select(p => new PlcAddressChange { Tag = p.Tag, Previous = before[p.Id], Address = p.Address })
                .ToList();
        });
        data.ChangedCount = committed.Result.Count;
        data.Changed = [.. committed.Result.Take(PlcReaddressRequest.MaxChanged)];
        data.Warning = committed.Warning;
        return data;
    }
}
