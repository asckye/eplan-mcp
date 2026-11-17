using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Creates shields, implemented by the EPLAN adapter layer. Main thread only.</summary>
public interface IShieldCreator
{
    /// <summary>The names of the SPECIAL library's shield symbols.</summary>
    public IReadOnlyList<string> Symbols(string projectPath);

    /// <summary>A13: a shield is a function, so it runs in a Transaction scope. The scope can reverse a commit, for the read-back after it.</summary>
    public ITransactionScopeFactory Transactions(string projectPath);

    /// <summary>Creates the shield, then a definition point on each wire point, inside the write's transaction.</summary>
    public ShieldObjects Create(string projectPath, string page, string symbol, string? name, ShieldCreateArgs area);

    /// <summary>Reads the shield back after the commit: never the object from inside the transaction (A36). EPLAN_OPERATION_FAILED when a definition point found no wire.</summary>
    public ShieldCreateData Read(string projectPath, string page, ShieldObjects objects);
}

/// <summary>The checked arguments of eplan_shield_create.</summary>
internal sealed class ShieldCreateRequest
{
    public const string DefaultSymbol = "SH";

    private const int MaxName = 256;
    private const int MaxWirePoints = 200;
    private const double MaxCoordinate = 10000;

    private ShieldCreateRequest(OpenProject project, string page, string symbol, string? name)
    {
        Project = project;
        Page = page;
        Symbol = symbol;
        Name = name;
    }

    public OpenProject Project { get; }

    public string Page { get; }

    public string Symbol { get; }

    public string? Name { get; }

    public static ShieldCreateRequest Check(ShieldCreateArgs args, IProjectCatalog catalog, IPageReader reader, IShieldCreator shields)
    {
        var violations = new List<string>();
        WriteArguments.RequireProject(args.Project, violations);
        if (string.IsNullOrEmpty(args.Page))
        { violations.Add("'page' is required: a full page name as eplan_page_list returns it."); }
        double[] corners = [args.X, args.Y, args.CornerX, args.CornerY];
        if (corners.Any(c => double.IsNaN(c) || c < 0 || c > MaxCoordinate))
        { violations.Add(FormattableString.Invariant($"'x', 'y', 'corner_x' and 'corner_y' must be from 0 to {MaxCoordinate} mm.")); }
        else if (args.X == args.CornerX || args.Y == args.CornerY)
        { violations.Add("'corner_x' and 'corner_y' must differ from 'x' and 'y': a shield has an area."); }
        if (args.WirePoints is { } points)
        {
            double[] xs = [Math.Min(args.X, args.CornerX), Math.Max(args.X, args.CornerX)];
            double[] ys = [Math.Min(args.Y, args.CornerY), Math.Max(args.Y, args.CornerY)];
            if (points.Length is < 1 or > MaxWirePoints || points.Any(p => p is not { Length: 2 } || p[0] < xs[0] || p[0] > xs[1] || p[1] < ys[0] || p[1] > ys[1]))
            { violations.Add(FormattableString.Invariant($"'wire_points' must hold 1 to {MaxWirePoints} [x, y] points inside the shield's area.")); }
        }
        if (args.Name is { } tag && (tag.Length is 0 or > MaxName || tag.Any(char.IsControl)))
        { violations.Add(FormattableString.Invariant($"'name' must be 1 to {MaxName} characters without controls.")); }
        if (violations.Count > 0)
        { throw WriteArguments.Invalid(violations); }
        OpenProject project = WriteArguments.Writable(catalog, args.Project);
        string page = PageUpdateRequest.Find(reader.PageNames(project.Path), args.Page!)
            ?? throw new BridgeErrorException(ErrorReason.PageNotFound, "No page has this name, so nothing was changed. Use eplan_page_list with name_prefix to find the exact page name.",
                retryable: false, new Dictionary<string, string> { ["project"] = project.Name, ["missing"] = args.Page! });
        string symbol = args.Symbol ?? DefaultSymbol;
        IReadOnlyList<string> known = shields.Symbols(project.Path);
        if (!known.Contains(symbol, StringComparer.Ordinal))
        { throw WriteArguments.Invalid(["'symbol' must be one of the SPECIAL library's shield symbols: " + string.Join(", ", known) + "."]); }
        string? name = args.Name is { } given && given.StartsWith("-", StringComparison.Ordinal) ? DevicePlaceRequest.DeviceStructureOf(page) + given : args.Name;
        return new ShieldCreateRequest(project, page, symbol, name);
    }

    public string StateFingerprint(ShieldCreateArgs args, IConnectionGenerator generator)
    {
        string[] pages = [Page];
        return Fingerprint.Of(new[] { Project.Path, Page, Symbol, Name ?? string.Empty, FormattableString.Invariant($"{args.X},{args.Y},{args.CornerX},{args.CornerY}") }
            .Concat((args.WirePoints ?? []).Select(p => FormattableString.Invariant($"{p[0]},{p[1]}")))
            .Concat(generator.ReadConnections(Project.Path, pages)[0].Select(ConnectionChanges.Key))
            .Concat(generator.ObjectIds(Project.Path, pages)[0]));
    }
}

/// <summary><see cref="OperationNames.ShieldCreatePlan"/>: the shield to create. Reads only.</summary>
public sealed class ShieldCreatePlanOperation(IProjectCatalog catalog, IPageReader reader, IShieldCreator shields, IConnectionGenerator generator) : Operation<ShieldCreateArgs, WritePlan>
{
    public override string Name => OperationNames.ShieldCreatePlan;

    protected override WritePlan Execute(ShieldCreateArgs args)
    {
        ShieldCreateRequest request = ShieldCreateRequest.Check(args, catalog, reader, shields);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes =
            [
                new PlannedChange
                {
                    Action = "create",
                    Kind = "shield",
                    Name = request.Name ?? request.Symbol,
                    Property = "area",
                    NewValue = FormattableString.Invariant($"{args.X},{args.Y} - {args.CornerX},{args.CornerY} on {request.Page}"),
                },
            ],
            Fingerprint = request.StateFingerprint(args, generator),
        };
    }
}

/// <summary>
/// <see cref="OperationNames.ShieldCreateExecute"/>: checks the plan's fingerprint again, creates the shield and the
/// definition points, then generates the page's connections, all in one undo step. EPLAN links a shield to a wire only
/// through a definition point placed after the shield (A112). A point on no wire reverses the call; a shield without wire
/// points is committed with a warning.
/// </summary>
public sealed class ShieldCreateExecuteOperation(IProjectCatalog catalog, IPageReader reader, IShieldCreator shields, IConnectionGenerator generator)
    : Operation<ShieldCreateArgs, ShieldCreateData>, IWriteOperation
{
    public const string Tool = "eplan_shield_create";

    public override string Name => OperationNames.ShieldCreateExecute;

    protected override ShieldCreateData Execute(ShieldCreateArgs args)
    {
        ShieldCreateRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            { throw InvalidArgument("'fingerprint' is required: the one shield.create.plan returned."); }
            request = ShieldCreateRequest.Check(args, catalog, reader, shields);
            if (!string.Equals(request.StateFingerprint(args, generator), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(ErrorReason.PlanStale, "Objects or connections on this page changed after the plan was made, so no shield was created. Call eplan_shield_create again for a new plan.",
                    retryable: false, new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        string path = request.Project.Path;
        ShieldCreateData data = new();
        Committed<ShieldObjects> committed = TransactionRunner.Run(
            shields.Transactions(path),
            Tool,
            () => shields.Create(path, request.Page, request.Symbol, request.Name, args),
            _ => true,
            objects => new OperationContext(OperationContext.Current.RequestId, true, null).Run(() =>
            {
                // A52/D5: generation only sees committed placement; the undo step spans the committed transactions.
                generator.Generate(path, [request.Page]);
                data = shields.Read(path, request.Page, objects);
                return true;
            }));
        data.Project = request.Project.Name;
        data.Warning = committed.Warning ?? (data.Connections.Count == 0 ? "The shield covers no connection: give 'wire_points', a point on each wire inside its area." : null);
        return data;
    }
}
