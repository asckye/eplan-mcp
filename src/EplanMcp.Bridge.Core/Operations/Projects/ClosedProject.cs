using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// What a project that is not open must be before the Bridge opens it read-only for one call
/// (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.1). Each check keeps EPLAN from a state it would not come back
/// from without the engineer: a dialog over a broken copy, or an upgrade of a project from another version.
/// </summary>
internal static class ClosedProject
{
    /// <summary>What <c>GetProjectDataCompatibility</c> answers for a project of this EPLAN version (api-notes A31).</summary>
    public const string Compatible = "IsCompatible";

    private static readonly string[] _sessionFiles = ["User.eod", "User.eox"];

    /// <summary>The checked link file of <paramref name="project"/>, or the error that says why it must not be opened.</summary>
    public static string Check(string? project, IProjectFiles files)
    {
        string path = project?.Trim() ?? string.Empty;
        if (!ProjectPaths.IsPlainFullPath(path) || !path.EndsWith(".elk", StringComparison.OrdinalIgnoreCase))
        {
            throw Invalid("with 'openReadOnly', 'project' must be the full local path of the .elk file of a project that is not open.");
        }

        string data = path.Substring(0, path.Length - 4) + ".edb";
        if (!File.Exists(path) || !Directory.Exists(data))
        {
            throw new BridgeErrorException(ErrorReason.ProjectNotOpen, "No project is open under this path, and none is in its folder either.");
        }

        // api-notes A35: without these EPLAN stops at a modal dialog ("the database index must be repaired") until
        // someone closes it.
        string[] missing = [.. _sessionFiles.Where(file => !File.Exists(Path.Combine(data, file)))];
        if (missing.Length > 0)
        {
            BridgeErrorException error = Invalid("the project's data folder lacks EPLAN's session files, so EPLAN would stop at a dialog while opening it. Copy the project again with every file of its data folder.");
            error.Error.Metadata!["missing"] = string.Join("\n", missing);
            throw error;
        }

        (string status, _) = files.Compatibility(path);
        if (!string.Equals(status, Compatible, StringComparison.Ordinal))
        {
            // Opening a project of another version may upgrade it for good (M6 design 1.2, item 5).
            BridgeErrorException error = Invalid("EPLAN does not report the project as compatible with this version, and opening it might upgrade it, so it was not opened.");
            error.Error.Metadata!["status"] = status;
            throw error;
        }

        return path;
    }

    private static BridgeErrorException Invalid(string violation) => new(
        ErrorReason.InvalidArgument,
        "Invalid arguments: " + violation,
        retryable: false,
        new Dictionary<string, string> { ["violations"] = violation });
}
