using System.Globalization;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Stable drawing identities, with no database/session prefix or raw handle (A32/A54, S42 r6).</summary>
public static class DrawingLocators
{
    /// <summary>Parses the adapter's identifier; only placement identifiers with index zero are supported.</summary>
    public static DrawingLocator FromIdentifier(string project, string page, string identifier, string digest)
    {
        int type = SnapshotCanonical.TypeOf(identifier);
        string[] parts = identifier.Split('/');
        if (type <= 0 || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out long oid) || oid <= 0 || parts[3] != "0"
            || string.IsNullOrWhiteSpace(project) || string.IsNullOrWhiteSpace(page) || digest.Length != 64 || digest.Any(c => c is not (>= '0' and <= '9') and not (>= 'a' and <= 'f')))
        {
            throw WriteArguments.Invalid(["/locator must contain a project, page, positive type/number, index zero and SHA-256 digest."]);
        }

        return new DrawingLocator { Project = project, Page = page, Type = type, Oid = oid, Digest = digest };
    }

    /// <summary>Checks the exact project, page, type, number and digest after re-reading; never substitutes another object.</summary>
    public static bool Matches(DrawingLocator expected, DrawingLocator actual) => ProjectResolver.SamePath(expected.Project, actual.Project)
        && expected.Page == actual.Page && expected.Type == actual.Type && expected.Oid == actual.Oid && expected.Digest == actual.Digest;
}
