namespace EplanMcp.Host.ClientConfig;

/// <summary>
/// What an edit does to a client's file. <see cref="Text"/> is the new content, or null when the file stays as it is.
/// <see cref="Before"/> and <see cref="After"/> show the server's entry in the file's own syntax, null where there is
/// none: only the entry, never the rest of the file, which may hold other servers' secrets. <see cref="CommentsDropped"/>:
/// the file had JSONC comments, which a rewrite cannot keep.
/// </summary>
internal sealed record ClientFileEdit(string? Text, string? Before, string? After, bool CommentsDropped = false);
