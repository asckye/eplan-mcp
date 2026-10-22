using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Host.Tests.Tools;

/// <summary>Real Bridge operations with file-producing fake EPLAN adapters, including in-execute compatibility.</summary>
internal sealed class ProjectCreateBridge : IProjectFactory, IProjectFiles
{
    public string Status { get; set; } = "IsCompatible";
    public Exception? CheckFailure { get; set; }
    public bool MakeFiles { get; set; } = true;
    public Action<string>? BeforeCheck { get; set; }
    public List<string> Events { get; } = [];

    public static object Plan(NewProjectArgs args, string mode)
    {
        Assert.False(Directory.Exists(args.Destination));
        return ConvertErrors(() => mode == "backup" ? new ProjectRestorePlanOperation().Execute((object)args)! : new ProjectCreatePlanOperation().Execute((object)args)!);
    }

    public object Execute(NewProjectArgs args, string mode) => ConvertErrors(() => mode == "backup"
        ? new ProjectRestoreExecuteOperation(this, this).Execute((object)args)!
        : new ProjectCreateExecuteOperation(this, this).Execute((object)args)!);

    public (int Pages, string? ProjectGuid) Create(string linkFile, string basicProject)
    {
        Make(linkFile);
        return (1, "basic@1");
    }

    public void Restore(string archive, string linkFile) => Make(linkFile);

    public (string Status, string DatabaseVersion) Compatibility(string linkFile)
    {
        Events.Add("compatibility:" + linkFile);
        Assert.True(File.Exists(linkFile));
        Assert.True(Directory.Exists(Path.ChangeExtension(linkFile, ".edb")));
        BeforeCheck?.Invoke(linkFile);
        if (CheckFailure is { } failure)
        { throw failure; }
        return (Status, "18.0");
    }

    private void Make(string linkFile)
    {
        Events.Add("make:" + linkFile);
        Assert.True(Directory.Exists(Path.GetDirectoryName(linkFile)));
        if (!MakeFiles)
        { return; }
        File.WriteAllText(linkFile, "new");
        Directory.CreateDirectory(Path.ChangeExtension(linkFile, ".edb"));
    }

    private static object ConvertErrors(Func<object> run)
    {
        try
        { return run(); }
        catch (BridgeErrorException ex) { throw new BridgeCallException(ex.Error); }
    }
}
