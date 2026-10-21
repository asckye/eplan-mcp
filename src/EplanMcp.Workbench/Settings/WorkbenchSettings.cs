using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using TiaOpenness.Gui.Localization;
using TiaOpenness.Gui.Themes;

namespace EplanMcp.Workbench.Settings;

/// <summary>
/// What the engineer chose in the settings drawer (workbench-ui section 7), kept in <c>workbench\settings.json</c> of
/// the data root. Nothing here changes how approvals work (APR-16): the deadline and the channels are the Host's.
/// </summary>
/// <param name="Language">The interface language; null follows the system's (LOC-10).</param>
/// <param name="Theme">Follow Windows, light or dark (COL-09); high contrast follows Windows regardless (COL-02).</param>
/// <param name="StartAtLogin">
/// Whether the Workbench starts when the engineer logs in. Remembered only: the installer writes the Run entry (M5
/// design 4.3, step 11).
/// </param>
internal sealed record WorkbenchSettings(AppLanguage? Language = null, AppTheme Theme = AppTheme.Auto, bool StartAtLogin = false)
{
    private static readonly JsonSerializerOptions _options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    /// <summary>The settings in <paramref name="path"/>; the defaults when the file is missing, unreadable or not valid.</summary>
    public static WorkbenchSettings Load(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize<WorkbenchSettings>(File.ReadAllBytes(path), _options) ?? new() : new();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new(); // a broken settings file must not keep the approval channel from starting
        }
    }

    /// <summary>Writes the settings whole (a temporary file, then a rename); false when that failed.</summary>
    public bool Save(string path)
    {
        string temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(temporary, JsonSerializer.SerializeToUtf8Bytes(this, _options));
            File.Move(temporary, path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false; // the choice still applies to this run
        }
    }
}
