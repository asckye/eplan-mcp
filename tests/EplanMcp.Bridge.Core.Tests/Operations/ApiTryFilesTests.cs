using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Medium")]
public sealed class ApiTryFilesTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("api-try-files-").FullName;
    private readonly FakeTransactions _transactions = new();
    private readonly FileDispatcher _dispatch = new();
    private readonly Catalog _catalog = new();
    private ApiTryPlanOperation Planner() => new(2026, _catalog, _dispatch);
    private ApiTryExecuteOperation Executor() => new(Planner(), _dispatch, _transactions);
    public void Dispose() => Directory.Delete(_root, true);
    private ApiTryArgs Args(bool output = false)
    {
        Directory.CreateDirectory(Path.Combine(_root, "imports"));
        File.WriteAllText(Path.Combine(_root, "imports", "data.xml"), "before");
        var locator = new JObject { ["project"] = @"D:\scratch\Scratch", ["page"] = "", ["type"] = 1, ["oid"] = 1, ["digest"] = new string('a', 64) };
        return new()
        {
            Group = "O:Eplan.EplApi.HEServices.PrePlanningService." + (output ? "Export" : "Import") + "PropertyDefinitions",
            Project = "Scratch",
            DataRoot = _root,
            OutputId = Guid.NewGuid().ToString("N"),
            Parameters = new JObject { ["target"] = locator, ["pProject"] = locator, ["strFileName"] = "data.xml" }.ToString()
        };
    }
    private void Approve(ApiTryArgs args) => args.Fingerprint = ((WritePlan)Planner().Execute(args)!).Fingerprint;

    [Fact]
    public void ChangedInputContentOrDestinationAfterApprovalNeverInvokes()
    {
        foreach (string change in new[] { "input", "root", "id" })
        {
            ApiTryArgs args = Args();
            Approve(args);
            if (change == "input")
            { File.WriteAllText(Path.Combine(_root, "imports", "data.xml"), "change"); }
            else if (change == "root")
            {
                args.DataRoot = Path.Combine(_root, "other");
                Directory.CreateDirectory(Path.Combine(args.DataRoot, "imports"));
                File.WriteAllText(Path.Combine(args.DataRoot, "imports", "data.xml"), "before");
            }
            else
            { args.OutputId = Guid.NewGuid().ToString("N"); }
            BridgeError error = Assert.Throws<BridgeErrorException>(() => Executor().Execute(args)).Error;
            Assert.Equal((ErrorReason.PlanStale, ExecutionState.NotStarted), (error.Reason, error.Execution));
        }
        Assert.Equal(0, _dispatch.Invocations);
        Assert.Empty(_transactions.Calls);
    }

    [Fact]
    public void InputWriteUsesOneTransactionAndDoesNotReplaceRawRelativeParameters()
    {
        ApiTryArgs args = Args();
        string raw = args.Parameters;
        Approve(args);
        var data = (ApiTryData)Executor().Execute(args)!;
        Assert.Equal(raw, args.Parameters);
        Assert.Equal(data.Before["input:strFileName"], data.After["input:strFileName"]);
        Assert.Empty(data.Files);
        Assert.Equal(["Begin AI: eplan_api_try", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
    }

    [Fact]
    public void OutputAndSidecarAreHashedBeforeSingleCommitAndExistingDestinationsAreRefused()
    {
        ApiTryArgs args = Args(output: true);
        Approve(args);
        var data = (ApiTryData)Executor().Execute(args)!;
        Assert.Equal(2, data.Files.Count);
        Assert.All(data.Files, f =>
        {
            Assert.StartsWith("api-try-" + args.OutputId + "/", f.Path, StringComparison.Ordinal);
            Assert.False(Path.IsPathRooted(f.Path));
            Assert.Equal(64, f.Sha256.Length);
            Assert.True(f.Bytes > 0);
        });
        string file = Path.Combine(_root, "exports", data.Files[0].Path);
        Assert.Equal(data.Files[0].Sha256, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(file))));
        Assert.Equal(["Begin AI: eplan_api_try", "Commit", "CloseUndo", "Dispose"], _transactions.Calls);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Planner().Execute(args)).Error.Reason);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingInputOrInputJunctionIsRejectedBeforeInvocation(bool junction)
    {
        ApiTryArgs args = Args();
        if (junction)
        {
            string outside = Path.Combine(_root, "outside");
            Directory.Move(Path.Combine(_root, "imports"), outside);
            // Junctions need no symlink privilege on Windows.
            using System.Diagnostics.Process process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c mklink /J \"" + Path.Combine(_root, "imports") + "\" \"" + outside + "\"") { CreateNoWindow = true, UseShellExecute = false })!;
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);
        }
        else
        { File.Delete(Path.Combine(_root, "imports", "data.xml")); }
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Planner().Execute(args)).Error.Reason);
        Assert.Equal(0, _dispatch.Invocations);
        if (junction)
        { Directory.Delete(Path.Combine(_root, "imports")); }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("oversize")]
    [InlineData("many")]
    [InlineData("throw")]
    [InlineData("link")]
    public void InvalidOutputOrInvocationFailureRollsBackWithoutCommit(string failure)
    {
        ApiTryArgs args = Args(output: true);
        _dispatch.Failure = failure;
        Approve(args);
        try
        {
            BridgeError error = Assert.Throws<BridgeErrorException>(() => Executor().Execute(args)).Error;
            Assert.Equal((ErrorReason.EplanOperationFailed, ExecutionState.RolledBack), (error.Reason, error.Execution));
            Assert.Contains("Rollback", _transactions.Calls);
            Assert.DoesNotContain("Commit", _transactions.Calls);
        }
        finally
        {
            if (failure == "link")
            { Directory.Delete(Path.Combine(_root, "exports", "api-try-" + args.OutputId, "link")); }
        }
    }

    private sealed class Catalog : IProjectCatalog
    {
        public IReadOnlyList<OpenProject> ListOpenProjects() => [new() { Name = "Scratch", Path = @"D:\scratch\Scratch", Current = true }];
    }
    private sealed class FileDispatcher : IApiTryDispatcher
    {
        public int Invocations { get; private set; }
        public string? Failure { get; set; }
        public void CheckTarget(ApiTryArgs args) { }
        public Dictionary<string, string> Inspect(ApiTryArgs args) => new() { ["snapshot:1"] = "unchanged" };
        public string? Read(ApiTryArgs args) => null;
        public object? Invoke(ApiTryArgs args)
        {
            Invocations++;
            foreach (var parameter in ApiTryFiles.Paths(args))
            {
                string path = ApiTryFilePaths.Resolve(args.DataRoot, args.OutputId, parameter.File, parameter.Output, create: true);
                if (!parameter.Output || Failure == "missing")
                { continue; }
                File.WriteAllText(path, "artifact");
                File.WriteAllText(path + ".sidecar", "metadata");
                if (Failure == "oversize")
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Write);
                    stream.SetLength(ApiTryFilePaths.MaxBytes + 1);
                }
                if (Failure == "many")
                {
                    for (int i = 0; i < 100; i++)
                    { File.WriteAllText(path + i.ToString(System.Globalization.CultureInfo.InvariantCulture), "a"); }
                }
                if (Failure == "link")
                {
                    string outside = Directory.CreateDirectory(Path.Combine(args.DataRoot, "outside")).FullName;
                    using System.Diagnostics.Process process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c mklink /J \"" + Path.Combine(Path.GetDirectoryName(path)!, "link") + "\" \"" + outside + "\"") { CreateNoWindow = true, UseShellExecute = false })!;
                    process.WaitForExit();
                    Assert.Equal(0, process.ExitCode);
                }
            }
            return Failure == "throw" ? throw new InvalidOperationException("trial failure") : null;
        }
    }
}
