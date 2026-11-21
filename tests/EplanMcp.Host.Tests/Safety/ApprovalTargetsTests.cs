using EplanMcp.Contracts;
using EplanMcp.Host.Safety;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Safety;

/// <summary>
/// ADR-0064 decision 2: the target of a destructive question comes from the resolved write plan, for the listed tools
/// only; a change name that is not an identifier is never offered as one.
/// </summary>
[Trait("Size", "Small")]
public sealed class ApprovalTargetsTests
{
    [Fact]
    public void DeletingOnePage_NamesThePage_AndManyPages_GiveTheirCount()
    {
        Assert.Equal(("page", "=A1&EFS1/5", 1, "delete"), Shape(ApprovalTargets.For(PageDestructiveTools.DeleteTool, Plan("page", "=A1&EFS1/5"))));
        Assert.Equal(("page", (string?)null, 2, "delete"), Shape(ApprovalTargets.For(PageDestructiveTools.DeleteTool, Plan("page", "=A1&EFS1/5", "=A1&EFS1/6"))));
    }

    [Fact]
    public void APlacement_IsConfirmedByCount_SinceItsChangeNameCarriesAKind()
    {
        Assert.Equal(("placement", (string?)null, 1, "delete"), Shape(ApprovalTargets.For(PlacementDeleteTools.Tool, Plan("function", "=A1-K1 (Function)"))));
        Assert.Equal(("device", (string?)null, 3, "renumber"), Shape(ApprovalTargets.For(DeviceDestructiveTools.RenumberTool, Plan("device", "-K1", "-K2", "-K3"))));
    }

    [Theory]
    [InlineData("=A1&EFS1/5\u200B")]
    [InlineData("0123456789012345678901234567890123456789012345678901234567890123456789")]
    public void AnUnusableName_IsNotOffered(string name) =>
        Assert.Null(ApprovalTargets.For(PageDestructiveTools.DeleteTool, Plan("page", name)).Target!.Id);

    [Fact]
    public void AToolNotListed_OrAPlanWithoutChanges_SaysNothing()
    {
        Assert.Equal((null, null), ApprovalTargets.For("eplan_part_sync", Plan("part", "SIE.1")));
        Assert.Equal((null, null), ApprovalTargets.For(PageDestructiveTools.DeleteTool, Plan("page")));
    }

    private static (string, string?, int, string?) Shape((Workbench.Protocol.ApprovalTarget? Target, string? Action) result) =>
        (result.Target!.Kind, result.Target.Id, result.Target.Count, result.Action);

    private static WritePlan Plan(string kind, params string[] names) => new()
    {
        Project = "Line1",
        ProjectPath = @"D:\Projects\Line1",
        Changes = [.. names.Select(n => new PlannedChange { Action = "delete", Kind = kind, Name = n })],
        Fingerprint = "f1",
    };
}
