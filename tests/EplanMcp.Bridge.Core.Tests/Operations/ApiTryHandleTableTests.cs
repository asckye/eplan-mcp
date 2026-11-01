using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ApiTryHandleTableTests
{
    private DateTime _now = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private const string Project = @"D:\scratch\Copy.elk";
    private const string Type = "Eplan.EplApi.DataModel.Pin";
    private sealed class Borrowed : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
        public override string ToString() => "Secret project contents";
    }
    private static object Resolve(ApiTryHandleTable table, ApiObjectHandle handle, string session = "host", bool scratch = true, bool compatible = true, bool write = false) =>
        table.Resolve(session, handle, Type, Project, (_, _) => compatible, _ => scratch, out _, write).Value;
    [Fact]
    public void TokenIsRandom128BitsAndMetadataNeverFormatsContents()
    {
        var table = new ApiTryHandleTable(() => _now);
        using var native = new Borrowed();
        ApiObjectHandle first = table.Add("host", native, Type, "O:Connection.StartPin", () => Project);
        ApiObjectHandle second = table.Add("host", native, Type, "O:Connection.EndPin", () => Project);
        Assert.True(ApiObjectHandle.Valid(first.Handle, first.Type));
        Assert.NotEqual(first.Handle, second.Handle);
        Assert.Same(native, table.Resolve("host", first, Type, Project, (_, _) => true, _ => true, out string text).Value);
        Assert.Equal(Type + " returned by O:Connection.StartPin", text);
        Assert.DoesNotContain("Secret", text, StringComparison.Ordinal);
        table.Clear();
        Assert.False(native.Disposed);
    }
    [Fact]
    public void IdleLifetimeIsTenMinutesAndSuccessfulUseRefreshesIt()
    {
        var table = new ApiTryHandleTable(() => _now);
        ApiObjectHandle handle = table.Add("host", new object(), Type, "O:source", () => Project);
        _now = _now.AddMinutes(9);
        Resolve(table, handle);
        _now = _now.AddMinutes(9);
        table.Sweep(_ => true);
        Resolve(table, handle);
        _now = _now.AddMinutes(10);
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Resolve(table, handle)).Error.Reason);
        table.Sweep(_ => true);
        _now = _now.AddHours(-1);
        Assert.Throws<BridgeErrorException>(() => Resolve(table, handle));
    }
    [Fact]
    public void LimitIsPerSessionAndEvictsOldestEvenIfRecentlyUsed()
    {
        var table = new ApiTryHandleTable(() => _now);
        ApiObjectHandle first = table.Add("host", new object(), Type, "O:source", () => Project);
        ApiObjectHandle other = table.Add("other", new object(), Type, "O:source", () => Project);
        for (int i = 0; i < 499; i++)
        { table.Add("host", new object(), Type, "O:source", () => Project); }
        Resolve(table, first);
        ApiObjectHandle latest = table.Add("host", new object(), Type, "O:source", () => Project);
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Resolve(table, first)).Error.Reason);
        Resolve(table, latest);
        Resolve(table, other, session: "other");
    }
    [Fact]
    public void SessionsProcessRestartAndCloseInvalidationCannotReuseHandles()
    {
        var table = new ApiTryHandleTable(() => _now);
        ApiObjectHandle handle = table.Add("host", new object(), Type, "O:source", () => Project);
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Resolve(table, handle, session: "restarted")).Error.Reason);
        Assert.Throws<BridgeErrorException>(() => Resolve(new(() => _now), handle));
        table.Sweep(_ => false);
        Assert.Throws<BridgeErrorException>(() => Resolve(table, handle));
        handle = table.Add("host", new object(), Type, "O:source", () => Project);
        table.Clear();
        Assert.Throws<BridgeErrorException>(() => Resolve(table, handle));
    }
    [Fact]
    public void TypeAndOwnershipAreRecheckedOnEveryUse()
    {
        var table = new ApiTryHandleTable(() => _now);
        string? owner = Project;
        int checks = 0;
        ApiObjectHandle handle = table.Add("host", new object(), Type, "O:source", () => { checks++; return owner; });
        Resolve(table, handle);
        Resolve(table, handle);
        Assert.Equal(2, checks);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Resolve(table, handle, compatible: false)).Error.Reason);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => Resolve(table, new() { Handle = handle.Handle, Type = "Eplan.EplApi.DataModel.Function" })).Error.Reason);
        Assert.Equal(ErrorReason.PolicyDenied, Assert.Throws<BridgeErrorException>(() => Resolve(table, handle, scratch: false)).Error.Reason);
        owner = @"D:\real\Real.elk";
        Assert.Equal(ErrorReason.PolicyDenied, Assert.Throws<BridgeErrorException>(() => Resolve(table, handle)).Error.Reason);
        owner = null;
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Resolve(table, handle)).Error.Reason);
    }
    [Fact]
    public void InvalidNativeObjectBecomesStaleAndSettingsCannotBeWritten()
    {
        var table = new ApiTryHandleTable(() => _now);
        ApiObjectHandle invalid = table.Add("host", new object(), Type, "O:source", () => throw new InvalidOperationException());
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Resolve(table, invalid)).Error.Reason);
        ApiObjectHandle settings = table.Add("host", new object(), Type, "O:source", () => Project, readOnly: true);
        Resolve(table, settings);
        Assert.Equal(ErrorReason.PolicyDenied, Assert.Throws<BridgeErrorException>(() => Resolve(table, settings, write: true)).Error.Reason);
        Assert.True(table.Resolve("host", settings, Type, Project, (_, _) => true, _ => true, out _).ReadOnly);
    }
    [Fact]
    public void ProjectCloseDuringOwnershipCheckCannotResurrectAnEntry()
    {
        var table = new ApiTryHandleTable(() => _now);
        ApiObjectHandle handle = table.Add("host", new object(), Type, "O:source", () => { table.Clear(); return Project; });
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Resolve(table, handle)).Error.Reason);
        Assert.Throws<BridgeErrorException>(() => Resolve(table, handle));
    }
}
