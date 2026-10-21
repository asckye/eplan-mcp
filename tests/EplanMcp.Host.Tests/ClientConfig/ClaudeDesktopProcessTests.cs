using EplanMcp.Host.ClientConfig;

namespace EplanMcp.Host.Tests.ClientConfig;

/// <summary>Claude Desktop's processes and Claude Code's share the name; the image path tells them apart.</summary>
[Trait("Size", "Small")]
public sealed class ClaudeDesktopProcessTests
{
    [Theory]
    [InlineData(@"C:\Program Files\WindowsApps\Claude_2.42304.0.0_x64__pzs8sxrjxfjjc\app\Claude.exe", true)]
    [InlineData(@"C:\Users\u\AppData\Local\AnthropicClaude\app-0.9.3\claude.exe", true)]
    [InlineData(null, true)]
    [InlineData(@"C:\Users\u\AppData\Roaming\Claude\claude-code\2.1.299\f0584be0fbaf\claude.exe", false)]
    [InlineData(@"C:\Users\u\.local\bin\claude.exe", false)]
    [InlineData(@"C:\Users\u\AppData\Roaming\npm\node_modules\@anthropic-ai\claude-code\bin\claude.exe", false)]
    public void IsDesktop_TellsTheAppFromTheCommandLine(string? imagePath, bool desktop)
    {
        Assert.Equal(desktop, ClaudeDesktopProcess.IsDesktop(imagePath));
    }
}
