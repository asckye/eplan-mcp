using EplanMcp.Workbench.Commands;
using EplanMcp.Workbench.Tests.Commands;
using EplanMcp.Workbench.Vm;

namespace EplanMcp.Workbench.Tests.Vm;

internal sealed class FakeVm : IVmElevation
{
    public const string Key = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG";
    public const string EmptyRules = "{\"schemaVersion\":1,\"rules\":[]}";
    public FakeHost Host { get; } = new();
    public List<(string Executable, bool Remove, string Port, string Address)> Elevated { get; } = [];
    public int? ExitCode { get; set; } = 0;
    public Exception? Error { get; set; }
    public List<string> Questions { get; } = [];
    public List<string> Clipboard { get; } = [];
    public bool Confirmed { get; set; } = true;
    public string? StoredKey { get; set; }
    public string Rules { get; set; } = EmptyRules;
    public string Instances { get; set; } = "{\"schemaVersion\":1,\"processes\":[100]}";
    public VmView Create(bool available = true, IVmLauncher? launcher = null)
    {
        Host.Answer = args => args[0] == "vm-instances" ? new HostRun(0, Instances) : args[0] == "vm-firewall" ? new HostRun(0, Rules) : args[1] switch
        {
            "show" => new HostRun(StoredKey is null ? 1 : 0, StoredKey ?? "VM key unavailable."),
            "create" => CreateKey(),
            "delete" => DeleteKey(),
            _ => throw new InvalidOperationException("Unexpected command."),
        };
        return new VmView(available ? Host : null, this, ["192.168.56.2"], launcher) { HostAddress = "192.168.56.1" };
    }

    public Task RunAsync(VmView view, string action) => view.RunAsync(action, question => { Questions.Add(question); return Confirmed; }, Clipboard.Add, CancellationToken.None);

    public Task<int?> RunAsync(string executable, bool remove, string port, string hostAddress, CancellationToken cancellationToken)
    {
        Elevated.Add((executable, remove, port, hostAddress));
        return Error is null ? Task.FromResult(ExitCode) : Task.FromException<int?>(Error);
    }

    private HostRun CreateKey()
    {
        StoredKey = Key;
        return new HostRun(0, "VM key created.");
    }

    private HostRun DeleteKey()
    {
        StoredKey = null;
        return new HostRun(0, string.Empty);
    }
}
