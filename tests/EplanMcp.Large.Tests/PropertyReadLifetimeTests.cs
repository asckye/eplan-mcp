using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host;
using EplanMcp.Host.Bridge;

namespace EplanMcp.Large.Tests;

/// <summary>A36: stable repeated reads, working-set recovery and unchanged design files; no forced native GC.</summary>
[Trait("Size", "Large")]
[Trait("Host", "Offline")]
public sealed class PropertyReadLifetimeTests(LiveEplan eplan)
{
    [Fact]
    public async Task SameBatch_200Rounds_WorkingSetRecoversAndDesignFilesStayUnchanged()
    {
        await using BridgeClient bridge = await LiveSnapshots.BridgeAsync(eplan.ProcessId);
        await LiveProperties.DescribeEnvironmentAsync(bridge, eplan.ProcessId);
        Dictionary<string, string> before = DesignFiles();
        PropertyReadArgs project = LiveProperties.Args(eplan, true, 10013, 10014, 10901, 3010);
        PropertyReadArgs page = LiveProperties.Args(eplan, false, 11011, 11901, 11029, 11024, 3012);
        string expected = await BatchAsync();
        for (int i = 0; i < 10; i++)
        {
            Assert.Equal(expected, await BatchAsync());
        }

        using Process process = Process.GetProcessById(eplan.ProcessId);
        long baseline = await SettledWorkingSetAsync(process);
        long peak = baseline;
        var clock = Stopwatch.StartNew();
        for (int round = 0; round < 200; round++)
        {
            Assert.Equal(expected, await BatchAsync());
            process.Refresh();
            peak = Math.Max(peak, process.WorkingSet64);
        }

        // Native allocators may retain some pages. A growth of >32 MiB or >5% of the warm baseline is a failure,
        // even if the process survives. Take an idle window; never trim its working set to make the test pass.
        long recovered = await SettledWorkingSetAsync(process);
        long allowance = Math.Max(32L * 1024 * 1024, baseline / 20);
        TestContext.Current.TestOutputHelper?.WriteLine($"200 rounds in {clock.Elapsed}; baseline {baseline} bytes; peak {peak}; "
            + $"recovered {recovered}; allowed retained growth {allowance}.");
        Assert.True(recovered <= baseline + allowance, $"A36 working set did not recover: {baseline} -> {recovered} bytes (peak {peak}).");
        Assert.Equal(before, DesignFiles());

        async Task<string> BatchAsync()
        {
            List<PropertyReadRow> projects = await LiveProperties.ReadAsync(bridge, project);
            List<PropertyReadRow> pages = await LiveProperties.ReadAsync(bridge, page);
            Assert.Contains(projects, r => r.PropertyId == 10013 && r.Status is "value" or "empty");
            Assert.Contains(pages, r => r.PropertyId == 11011 && r.Status == "value");
            Assert.Contains(pages, r => r.PropertyId == 3012 && r.Status == "read_refused");
            return JsonSerializer.Serialize(new[] { projects, pages }, HostJson.Options);
        }
    }

    private static async Task<long> SettledWorkingSetAsync(Process process)
    {
        long minimum = long.MaxValue;
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(250), TestContext.Current.CancellationToken);
            process.Refresh();
            minimum = Math.Min(minimum, process.WorkingSet64);
        }

        return minimum;
    }

    private Dictionary<string, string> DesignFiles()
    {
        Dictionary<string, string> files = LiveSnapshots.DesignFiles(eplan.ProjectPath);
        // A17/A35: User.* and .lck are session data, not design data; never delete or alter them.
        using var stream = new FileStream(eplan.ProjectPath + ".elk", FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        files.Add("project.elk", Convert.ToHexString(SHA256.HashData(stream)));
        return files;
    }
}
