using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core.Diagnostics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Adapters;

#pragma warning disable CA2000 // All fake native objects are borrowed; disposal is forbidden by the adapters.

[Trait("Size", "Small")]
public sealed class NativePreconditionTests
{
    [Theory]
    [InlineData("database")]
    [InlineData("project")]
    [InlineData("page")]
    public void ProtectedText_RefusesBeforeUndockOrTransaction(string protection)
    {
        var page = new Page { WriteProtected = protection == "page" };
        var project = new Project(page) { IsReadOnly = protection == "project" };
        var text = new Text { IsReadOnly = protection == "database", Project = project, Page = page };
        var transactions = new FakeTransactions();
        BridgeError error = Assert.Throws<BridgeErrorException>(() =>
        {
            EplanNativePreconditions.Writable(text);
            TransactionRunner.Run(transactions, "eplan_api_try", () => { text.UndockText(); return true; });
        }).Error;
        Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.NotStarted), (error.Reason, error.Execution));
        Assert.Equal(0, text.UndockCalls);
        Assert.Empty(transactions.Calls);
    }

    [Fact]
    public void WritableText_AllowsUndock()
    {
        var text = new Text { Page = new Page(), Project = new Project() };
        EplanNativePreconditions.Writable(text);
        text.UndockText();
        Assert.Equal(1, text.UndockCalls);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void PageBinding_RequiresValidPlacedObjectAndValidPage(bool valid, bool placed, bool pageValid)
    {
        var function = new Function(1) { IsValid = valid, Page = placed ? new Page { IsValid = pageValid } : null! };
        Assert.Equal(valid && placed && pageValid, EplanNativePreconditions.HasPage(function));
    }

    [Fact]
    public void FunctionNames_InvalidFunctionIsSkippedBeforeNameAndCountedInWarning()
    {
        var invalid = new Function(1) { IsValid = false };
        var valid = new Function(2) { Name = "=A+L-K1" };
        EplanProjects.Project = new Project { UnplacedFunctions = [invalid, valid] };
        var log = new ListLog();
        try
        {
            new OperationContext("f94993e7", false, null, log).Run(() =>
            {
                Assert.Equal(["=A+L-K1"], new EplanDeviceRenamer().FunctionNames("project"));
                return null;
            });
            Assert.Equal(0, invalid.NameReads);
            var warning = Assert.Single(log.Entries);
            Assert.Equal(BridgeLogLevel.Warning, warning.Level);
            Assert.Contains("[f94993e7] Skipped 1 invalid", warning.Message, StringComparison.Ordinal);
        }
        finally { EplanProjects.Project = null; }
    }
}
