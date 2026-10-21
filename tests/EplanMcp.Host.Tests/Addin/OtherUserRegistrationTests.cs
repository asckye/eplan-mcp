using EplanMcp.Host.Addin;

namespace EplanMcp.Host.Tests.Addin;

/// <summary>Telling another user's add-in path from this user's (Q15). Small.</summary>
[Trait("Size", "Small")]
public sealed class OtherUserRegistrationTests
{
    [Theory]
    [InlineData(@"C:\Users\other\EplanMcp\app\bridge\net481\EplanMcp.Bridge.Eplan.dll", true)]
    [InlineData(@"c:\users\OTHER\x\EplanMcp.Bridge.Eplan.dll", true)]
    [InlineData(@"C:/Users/other/EplanMcp/app/bridge/net481/EplanMcp.Bridge.Eplan.dll", true)]
    [InlineData(@"C:\Users\工程师\..\other\EplanMcp.Bridge.Eplan.dll", true)]
    [InlineData(@"C:\Users\工程师2\EplanMcp.Bridge.Eplan.dll", true)]
    [InlineData(@"C:\Users\other\EplanMcp\app\bridge\net481", true)]
    [InlineData(@"C:\Users\工程师\EplanMcp\app\bridge\net481\EplanMcp.Bridge.Eplan.dll", false)]
    [InlineData(@"C:\USERS\工程师\EplanMcp\app\bridge\net481\EplanMcp.Bridge.Eplan.dll", false)]
    [InlineData(@"C:\Users\Public\EplanMcp.Bridge.Eplan.dll", false)]
    [InlineData(@"D:\Code\EplanMcp\src\EplanMcp.Bridge.Eplan\bin\Release\net481\EplanMcp.Bridge.Eplan.dll", false)]
    [InlineData(@"EplanMcp.Bridge.Eplan.dll", false)]
    [InlineData("?", false)]
    public void InAnotherProfile_IsAnotherUsersProfileOnly(string path, bool other)
    {
        Assert.Equal(other, OtherUserRegistration.InAnotherProfile(path, @"C:\Users\工程师\", @"C:\Users\Public"));
    }

    [Fact]
    public void InAnotherProfile_TakesThePublicFolderWhereverItIs()
    {
        Assert.True(OtherUserRegistration.InAnotherProfile(@"C:\Users\Public\x.dll", @"C:\Users\工程师", @"D:\Shared"));
        Assert.False(OtherUserRegistration.InAnotherProfile(@"C:\Users\Shared\x.dll", @"C:\Users\工程师", @"C:\Users\Shared"));
    }
}
