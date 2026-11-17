using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Reads only: one project update, with file facts and counts (docs/tools/eplan_import_plc.md, section 5).</summary>
public sealed class ImportPlcPlanOperation(IProjectCatalog catalog, IPlcImporter importer) : Operation<ImportPlcArgs, WritePlan>
{
    private readonly Action<ImportSource> _readSource = source => source.Read(hash: true, maxBytes: 100 * 1024 * 1024);

    internal ImportPlcPlanOperation(IProjectCatalog catalog, IPlcImporter importer, Action<ImportSource> readSource) : this(catalog, importer) => _readSource = readSource;

    public override string Name => OperationNames.ImportPlcPlan;

    protected override WritePlan Execute(ImportPlcArgs args)
    {
        var request = ImportPlcRequest.Check(args, catalog, importer, _readSource);
        return new WritePlan
        {
            Project = request.Project.Name,
            ProjectPath = request.Project.Path,
            Changes = [new() { Action = "update", Kind = "project", Property = "PLC bus data", Name = request.Project.Name, OldValue = request.Source.Describe() + "; file preflight is incomplete; new object names will be checked against pre-import full names inside the transaction; any match rolls the import back." }],
            Fingerprint = request.StateFingerprint(),
            Details = new Dictionary<string, string>
            {
                ["file"] = request.Source.File,
                ["sha256"] = request.Source.Sha256,
                ["bytes"] = request.Source.Bytes.ToString(CultureInfo.InvariantCulture),
                ["converter"] = request.Converter,
                ["importMatch"] = "0",
                ["preflightComplete"] = "false",
                ["sourceDeviceCount"] = "0",
                ["functions"] = request.Before.Counts.Functions.ToString(CultureInfo.InvariantCulture),
                ["plcBoxes"] = request.Before.Counts.PlcBoxes.ToString(CultureInfo.InvariantCulture),
                ["articleReferences"] = request.Before.Counts.ArticleReferences.ToString(CultureInfo.InvariantCulture),
            },
        };
    }
}
