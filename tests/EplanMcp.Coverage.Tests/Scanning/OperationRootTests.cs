using System.Reflection;
using EplanMcp.Contracts;
using EplanMcp.Coverage.Scanning;

namespace EplanMcp.Coverage.Tests.Scanning;

[Trait("Size", "Small")]
public sealed class OperationRootTests
{
    private static readonly List<OperationRoot> _roots = OperationRoot.FromBridgeCore();

    [Fact]
    public void FromBridgeCore_FindsEveryOperationName_ExceptTheRoutersOwn()
    {
        string[] names = [.. typeof(OperationNames).GetFields(BindingFlags.Public | BindingFlags.Static).Select(f => (string)f.GetRawConstantValue()!)];

        Assert.Equal(
            names.Except([OperationNames.OutcomeGet, OperationNames.OperationCancel]).Order(StringComparer.Ordinal),
            _roots.Select(r => r.Name).Distinct().Order(StringComparer.Ordinal));
    }

    [Fact]
    public void FromBridgeCore_ASharedClass_GivesOneOperationPerKind()
    {
        OperationRoot pdf = Assert.Single(_roots, r => r.Name == OperationNames.ExportPdfPlan);
        OperationRoot labels = Assert.Single(_roots, r => r.Name == OperationNames.ExportLabelsRun);

        Assert.Equal(("EplanMcp.Bridge.Core.Operations.ExportPlanOperation", "EplanMcp.Bridge.Core.Operations.ExportKind", "Pdf"), (pdf.ClassName, pdf.KindType, pdf.KindValue));
        Assert.Equal(("EplanMcp.Bridge.Core.Operations.ProjectExportRunOperation", "Labels"), (labels.ClassName, labels.KindValue));
    }
}
