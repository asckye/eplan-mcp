using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests;

/// <summary>Complete, already filtered page inventories; deliberately has no write API.</summary>
internal sealed class FakePageTextReader : IPageTextReader
{
    private static readonly string[] _languages = ["en_US", "de_DE", "zh_CN"];

    public Dictionary<string, PageTextSnapshot> Pages { get; } = new(StringComparer.Ordinal);

    public List<(string Path, string Name)> Reads { get; } = [];

    public Action? OnRead { get; set; }

    public string? FailingPage { get; set; }

    public string? LanguageCode(string language) =>
        _languages.FirstOrDefault(l => string.Equals(l, language, StringComparison.OrdinalIgnoreCase));

    public PageTextSnapshot? Read(string projectPath, string pageName)
    {
        Reads.Add((projectPath, pageName));
        OnRead?.Invoke();
        if (string.Equals(FailingPage, pageName, StringComparison.Ordinal))
        {
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "The page text inventory could not be read completely; ask the engineer to check it.");
        }

        return Pages.GetValueOrDefault(pageName);
    }
}
