using EplanMcp.Packs.Scanning;

namespace EplanMcp.Host.Packs;

internal enum PackAction
{
    Scan,
    Check,
    Export,
    Install,
}

/// <summary>
/// <c>eplanmcp pack …</c>: scan takes inputs (a path, or <c>&lt;kind&gt;=path</c> / <c>sample=path</c>) and, with
/// <see cref="Out"/>, writes the pack, which needs <see cref="Identity"/>; check and install take one folder or file;
/// export takes the folder and the <c>.eplanpack</c> file.
/// </summary>
internal sealed record PackRequest(PackAction Action, IReadOnlyList<string> Paths, string? Out = null, PackIdentity? Identity = null, bool Official = false);
