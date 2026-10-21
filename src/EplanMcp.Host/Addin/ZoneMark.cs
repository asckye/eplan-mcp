namespace EplanMcp.Host.Addin;

/// <summary>
/// The download mark Windows puts on files from the internet, the <c>Zone.Identifier</c> stream. EPLAN cannot load an
/// add-in DLL that carries it (api-notes A23), so install-addin removes it first, as <c>Unblock-File</c> does.
/// </summary>
internal static class ZoneMark
{
    public const string Stream = ":Zone.Identifier";

    /// <summary>
    /// Removes the mark from every file in <paramref name="folder"/> and below, without following links; a folder that
    /// cannot be read throws rather than leaving marked files behind.
    /// </summary>
    public static void RemoveAll(string folder)
    {
        var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        foreach (string file in Directory.EnumerateFiles(folder, "*", options))
        {
            File.Delete(file + Stream); // no-op when the file has no mark
        }
    }
}
