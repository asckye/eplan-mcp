namespace EplanMcp.Contracts.Tests;

/// <summary>
/// The registered property set of the snapshot (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.2): no personal data,
/// nothing that changes on reopening, and every entry well formed.
/// </summary>
[Trait("Size", "Small")]
public sealed class SnapshotProfilesTests
{
    private static readonly string[] _scopes = ["page", "function", "terminal", "plc", "cable", "connection"];
    private static readonly string[] _kinds = [SnapshotProperty.Text, SnapshotProperty.MultiLanguage, SnapshotProperty.Hint];

    [Fact]
    public void Standard_HasNoPersonalDataAndNoRuntimeIdentifier()
    {
        // A36: 2000 (PROPUSER_DBOBJECTID) holds a runtime identifier; 3010-3013 are the last modifier's code, name,
        // e-mail and telephone.
        Assert.DoesNotContain(SnapshotProfiles.Standard, p => p.Name.StartsWith("PROPUSER_", StringComparison.Ordinal));
        Assert.DoesNotContain(SnapshotProfiles.Standard, p => p.Id is 2000 or (>= 3010 and <= 3013));
    }

    [Fact]
    public void Standard_EveryEntryIsWellFormed_AndEveryNumberOnce()
    {
        Assert.All(SnapshotProfiles.Standard, p => Assert.Contains(p.Scope, _scopes));
        Assert.All(SnapshotProfiles.Standard, p => Assert.Contains(p.Kind, _kinds));
        Assert.Equal(SnapshotProfiles.Standard.Count, SnapshotProfiles.Standard.Select(p => p.Id).Distinct().Count());
        Assert.Equal(19, SnapshotProfiles.Standard.Count);
    }

    [Fact]
    public void Standard_HintsAreThePagesModificationDateAndTimeOnly() =>
        Assert.Equal(
            ["PAGE_LASTAUTOMODIFICATIONDATE", "PAGE_LASTAUTOMODIFICATIONTIME"],
            SnapshotProfiles.Standard.Where(p => p.Kind == SnapshotProperty.Hint).Select(p => p.Name));
}
