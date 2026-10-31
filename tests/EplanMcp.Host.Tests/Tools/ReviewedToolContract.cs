using System.Text.Json;
using EplanMcp.Host.Tools;

namespace EplanMcp.Host.Tests.Tools;

internal static class ReviewedToolContract
{
    public static IReadOnlyList<string> Names { get; } = LoadNames();

    public static string[] For(ToolProfile profile = ToolProfile.Full, bool actionRun = true, bool apiTry = true, bool actionTry = true) =>
        [.. Names.Where(name => (profile == ToolProfile.Full || ToolProfiles.Core.Contains(name))
            && (actionRun || name != ActionRunTools.Tool)
            && (apiTry || name != ApiTryTools.Tool)
            && (actionTry || name != ActionTryTools.Tool))];

    private static string[] LoadNames()
    {
        // Embed the reviewed file so Small tests never access the file system or derive expectations from registration.
        using var reader = new StreamReader(typeof(ReviewedToolContract).Assembly.GetManifestResourceStream("Contracts.server-contract.verified.json")!);
        using JsonDocument snapshot = JsonDocument.Parse(reader.ReadToEnd());
        return [.. snapshot.RootElement.GetProperty("tools").EnumerateArray()
            .Select(tool => tool.GetProperty("name").GetString()!).Order(StringComparer.Ordinal)];
    }
}
