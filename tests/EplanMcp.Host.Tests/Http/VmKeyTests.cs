using System.Text;
using EplanMcp.Host.Http;

namespace EplanMcp.Host.Tests.Http;

[Trait("Size", "Medium")]
public sealed class VmKeyTests
{
    [Fact]
    public async Task Commands_StoreCiphertextShowExplicitlyRotateAndDelete()
    {
        var locations = new HostDataLocations(Path.Combine(Path.GetTempPath(), "EplanMcpKeyTests-" + Guid.NewGuid().ToString("N")));
        using var output = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        try
        {
            Assert.Equal(0, await VmKey.RunAsync("create", locations, output, TestContext.Current.CancellationToken));
            string first = VmKey.Read(locations);
            Assert.DoesNotContain(first, output.ToString(), StringComparison.Ordinal);
            byte[] stored = File.ReadAllBytes(Path.Combine(locations.Root, "vm-key.dpapi"));
            Assert.DoesNotContain(first, Encoding.UTF8.GetString(stored), StringComparison.Ordinal);
            output.GetStringBuilder().Clear();
            Assert.Equal(0, await VmKey.RunAsync("show", locations, output, TestContext.Current.CancellationToken));
            Assert.Equal(first + Environment.NewLine, output.ToString());
            Assert.Equal(0, await VmKey.RunAsync("create", locations, TextWriter.Null, TestContext.Current.CancellationToken));
            Assert.NotEqual(first, VmKey.Read(locations));
            Assert.Equal(0, await VmKey.RunAsync("delete", locations, TextWriter.Null, TestContext.Current.CancellationToken));
            Assert.False(File.Exists(Path.Combine(locations.Root, "vm-key.dpapi")));
            Assert.Equal(1, await VmKey.RunAsync("show", locations, TextWriter.Null, TestContext.Current.CancellationToken));
        }
        finally
        {
            if (Directory.Exists(locations.Root))
            {
                Directory.Delete(locations.Root, recursive: true);
            }
        }
    }
}
