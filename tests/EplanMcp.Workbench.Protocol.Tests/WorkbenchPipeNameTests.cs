using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;

namespace EplanMcp.Workbench.Protocol.Tests;

/// <summary>The pipe name (ADR-0014 decision 2): its form, its digest, and which inputs give the same or another name.</summary>
[Trait("Size", "Small")]
public sealed class WorkbenchPipeNameTests
{
    private const string Sid = "S-1-5-21-1004336348-1177238915-682003330-1001";

    [Fact]
    public void TheName_IsThePrefixAndTheFirst32HexDigitsOfTheDigest()
    {
        string expected = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Sid + "\n" + @"C:\USERS\ENGINEER\EPLANMCP")))[..32];

        string name = WorkbenchPipeName.For(Sid, @"C:\Users\Engineer\EplanMcp");

        Assert.Equal("EplanMcp.Workbench.1." + expected, name);
        Assert.Matches("^EplanMcp\\.Workbench\\.1\\.[0-9a-f]{32}$", name);
    }

    [Theory]
    [InlineData(@"c:\users\engineer\eplanmcp")]
    [InlineData(@"C:\Users\Engineer\EplanMcp\")]
    [InlineData(@"C:/Users/Engineer/EplanMcp")]
    [InlineData(@"C:\Users\Engineer\Documents\..\EplanMcp")]
    [InlineData(@"C:\Users\Engineer\.\EplanMcp")]
    public void SpellingsOfOneDataRoot_GiveOneName(string root)
    {
        Assert.Equal(WorkbenchPipeName.For(Sid, @"C:\Users\Engineer\EplanMcp"), WorkbenchPipeName.For(Sid, root));
    }

    [Fact]
    public void AnotherUserOrDataRoot_GivesAnotherName()
    {
        string name = WorkbenchPipeName.For(Sid, @"C:\Users\Engineer\EplanMcp");

        Assert.NotEqual(name, WorkbenchPipeName.For("S-1-5-21-1004336348-1177238915-682003330-1002", @"C:\Users\Engineer\EplanMcp"));
        Assert.NotEqual(name, WorkbenchPipeName.For(Sid, @"C:\Users\Engineer\EplanMcp-test"));
        Assert.NotEqual(name, WorkbenchPipeName.For(Sid, @"D:\Users\Engineer\EplanMcp"));
    }

    [Fact]
    public void TheCurrentUsersName_UsesTheirSid()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();

        Assert.Equal(WorkbenchPipeName.For(identity.User!.Value, @"C:\EplanMcp"), WorkbenchPipeName.ForCurrentUser(@"C:\EplanMcp"));
    }
}
