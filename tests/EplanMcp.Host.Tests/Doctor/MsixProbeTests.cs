using EplanMcp.Host.Doctor;

namespace EplanMcp.Host.Tests.Doctor;

/// <summary>The probe behind the MSIX check (spike S5), on temporary directories.</summary>
[Trait("Size", "Medium")]
public sealed class MsixProbeTests
{
    [Fact]
    public void FindRedirected_NamesThePackageWhoseLocalCacheHasTheProbe()
    {
        string packages = Directory.CreateTempSubdirectory("eplanmcp-doctor-").FullName;
        try
        {
            Directory.CreateDirectory(Path.Combine(packages, "Other_abc", "LocalCache", "Local"));
            string local = Directory.CreateDirectory(Path.Combine(packages, "Claude_pzs8sxrjxfjjc", "LocalCache", "Local")).FullName;
            File.WriteAllBytes(Path.Combine(local, "probe.tmp"), []);

            Assert.Equal("Claude_pzs8sxrjxfjjc", DoctorChecks.FindRedirected(packages, "probe.tmp"));
            Assert.Null(DoctorChecks.FindRedirected(packages, "other.tmp"));
            Assert.Null(DoctorChecks.FindRedirected(Path.Combine(packages, "missing"), "probe.tmp"));
        }
        finally
        {
            Directory.Delete(packages, recursive: true);
        }
    }

    [Fact]
    public void RedirectingPackage_NotRedirected_IsNullAndLeavesNoProbe()
    {
        string localAppData = Directory.CreateTempSubdirectory("eplanmcp-doctor-").FullName;
        try
        {
            Assert.Null(DoctorChecks.RedirectingPackage(localAppData));
            Assert.Empty(Directory.EnumerateFileSystemEntries(localAppData));
        }
        finally
        {
            Directory.Delete(localAppData, recursive: true);
        }
    }
}
