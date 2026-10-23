using System.Diagnostics;
using System.Text;
using EplanMcp.Host.Audit;
using EplanMcp.Host.Plans;

namespace EplanMcp.Host.Tests.Plans;

internal sealed class PlanStoreFixture : IDisposable
{
    public HostDataLocations Locations { get; } = new(Path.Combine(Path.GetTempPath(), "plan-tests-" + Guid.NewGuid().ToString("N")));
    public AuditLog Audit => new(Locations.Audit, TimeProvider.System);
    public PlanStore Store => new(Locations, Audit) { Clock = new PlanTestClock(PlanTestData.Created) }; // fixed, so retention never depends on today
    public string Root => Path.Combine(Locations.State, "plans");
    public const string Changes = "Complete changes\n";
    public PlanDefinition Definition { get; } = PlanTestData.Definition() with
    {
        Digests = PlanTestData.Definition().Digests with { ChangesDigest = PlanStore.Hash(Encoding.UTF8.GetBytes(Changes)) },
    };
    public string Record => Path.Combine(Root, Definition.PlanId + ".json");
    public Task<PlanRecord> Publish(PlanStore? store = null) => (store ?? Store).PublishAsync(Definition, Changes, new Dictionary<string, byte[]>(), TestContext.Current.CancellationToken);

    public static async Task Junction(string path, string target)
    {
        using Process process = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{path}\" \"{target}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        })!;
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, process.ExitCode);
    }
    public void Dispose()
    {
        if (Directory.Exists(Locations.Root))
        {
            Directory.Delete(Locations.Root, recursive: true);
        }
    }
}
