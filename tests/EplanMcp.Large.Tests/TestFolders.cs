namespace EplanMcp.Large.Tests;

/// <summary>Temporary folders of the live tests.</summary>
internal static class TestFolders
{
    /// <summary>Deletes a folder; EPLAN may hold its files for a moment, and a leftover must not fail the test.</summary>
    internal static void Delete(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TestContext.Current.TestOutputHelper?.WriteLine("Not deleted: " + folder + ": " + ex.Message);
        }
    }
}
