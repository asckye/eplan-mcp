using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.ProjectUpdateExecute"/>: checks the plan's fingerprint again, sets the properties in one
/// transaction and reads them back (ADR-0052). A read-back that differs rolls the write back.
/// </summary>
public sealed class ProjectUpdateExecuteOperation(IProjectCatalog catalog, IProjectProperties properties, ITextTranslator translator, ITransactionScopeFactory transactions)
    : Operation<ProjectUpdateArgs, ProjectUpdateData>, IWriteOperation
{
    public const string Tool = "eplan_project_update";

    public override string Name => OperationNames.ProjectUpdateExecute;

    protected override ProjectUpdateData Execute(ProjectUpdateArgs args)
    {
        ProjectUpdateRequest request;
        try
        {
            if (string.IsNullOrEmpty(args.Fingerprint))
            { throw InvalidArgument("'fingerprint' is required: the one project.update.plan returned."); }
            request = ProjectUpdateRequest.Check(args, catalog, properties, translator);
            if (!string.Equals(request.StateFingerprint(), args.Fingerprint, StringComparison.Ordinal))
            {
                throw new BridgeErrorException(ErrorReason.PlanStale, "The project's properties changed after the plan was made, so nothing was changed. Call eplan_project_update again for a new plan.",
                    retryable: false, new Dictionary<string, string> { ["project"] = request.Project.Name });
            }
        }
        catch (BridgeErrorException ex)
        {
            throw WriteArguments.NotStarted(ex);
        }

        var changing = request.Changing.ToList();
        var data = new ProjectUpdateData { Project = request.Project.Name };
        if (changing.Count == 0)
        { return data; }
        string path = request.Project.Path;
        Committed<List<ProjectPropertyChange>> committed = TransactionRunner.Run(transactions, Tool, () =>
        {
            foreach ((string key, _, string after) in changing)
            {
                OperationContext.Current.ThrowIfCancellationRequested();
                properties.Write(path, key, after);
            }
            var read = new List<ProjectPropertyChange>();
            foreach ((string key, string before, string after) in changing)
            {
                string actual = properties.Read(path, key);
                if (!TextLanguages.SameValue(actual, after))
                {
                    throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN did not store a property as asked, so nothing was committed. metadata.property names it.",
                        retryable: false, new Dictionary<string, string> { ["property"] = key });
                }
                read.Add(new ProjectPropertyChange { Property = key, Previous = before, Value = actual });
            }
            return read;
        });
        data.Properties = committed.Result;
        data.Warning = committed.Warning;
        return data;
    }
}
