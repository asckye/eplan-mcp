using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// <see cref="OperationNames.CheckList"/> (docs/tools/eplan_check_list.md): the check messages already stored in the
/// project, read in one main-thread call. The done filter, the counts, the severity filter and the slice happen
/// here on plain records, in EPLAN's order. Running a check is a write and belongs to M3 (M2 design Q2).
/// </summary>
public sealed class CheckListOperation(IProjectCatalog catalog, IProjectMessageReader reader) : Operation<CheckListArgs, CheckListData>
{
    public override string Name => OperationNames.CheckList;

    protected override CheckListData Execute(CheckListArgs args)
    {
        string? severity = null;
        if (!string.IsNullOrWhiteSpace(args.Severity))
        {
            string wanted = args.Severity!.Trim();
            severity = new[] { CheckMessage.Error, CheckMessage.Warning, CheckMessage.Hint }
                .FirstOrDefault(s => string.Equals(s, wanted, StringComparison.OrdinalIgnoreCase))
                ?? throw Invalid("'severity' must be error, warning or hint; omit it for every severity.");
        }

        OpenProject project = ProjectResolver.Resolve(catalog.ListOpenProjects(), args.Project);
        IReadOnlyList<ProjectMessageRecord> records = reader.ReadMessages(project.Path);
        List<CheckMessage> counted = [.. records.Where(r => args.IncludeDone || !r.Done).Select(ToMessage)];
        List<CheckMessage> matching = [.. counted.Where(m => severity is null || m.Severity == severity)];

        return new CheckListData
        {
            Project = project.Name,
            Items = Paging.Slice(matching, args),
            Total = matching.Count,
            Errors = counted.Count(m => m.Severity == CheckMessage.Error),
            Warnings = counted.Count(m => m.Severity == CheckMessage.Warning),
            Hints = counted.Count(m => m.Severity == CheckMessage.Hint),
            Done = records.Count(r => r.Done),
        };
    }

    /// <summary>The tool's severity for EPLAN's <c>IMessage.Classification</c> name.</summary>
    internal static string Severity(string category) => category switch
    {
        "Error" => CheckMessage.Error,
        "Warning" => CheckMessage.Warning,
        "Hint" => CheckMessage.Hint,
        _ => CheckMessage.Unknown,
    };

    private static CheckMessage ToMessage(ProjectMessageRecord record) => new()
    {
        Number = record.Id,
        Severity = Severity(record.Category),
        Region = record.Region,
        Text = record.Text,
        Done = record.Done,
        Target = record.ObjectKind is null ? null : new CheckObject { Kind = record.ObjectKind, Name = record.ObjectName },
    };

    private static BridgeErrorException Invalid(string violation) => new(
        ErrorReason.InvalidArgument,
        "Invalid arguments: " + violation,
        retryable: false,
        new Dictionary<string, string> { ["violations"] = violation });
}
