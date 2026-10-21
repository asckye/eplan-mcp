using System.Text.Json;

namespace EplanMcp.Host.Addin;

/// <summary>
/// A registration install-addin made: the EPLAN.exe that ran the command, its file version, the variant, the add-in DLL
/// and when.
/// </summary>
internal sealed record RecordedAddin(string Eplan, string Version, string Variant, string Dll, DateTimeOffset Time);

/// <summary>
/// <c>state\addins.json</c> (M5 design 4.2): the registrations install-addin made, one per EPLAN.exe and variant, which
/// uninstall-addin undoes. EPLAN keeps the setting keys after <c>/unregister</c> (spike S14), so this record, not EPLAN's
/// settings, says what to undo. Written to a temporary file and moved into place. A record that cannot be read is an
/// error, never an empty list: writing over it would forget registrations.
/// </summary>
internal sealed class AddinRecord(string stateDirectory)
{
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    public string FilePath => Path.Combine(stateDirectory, "addins.json");

    public static AddinRecord For(HostDataLocations locations) => new(locations.State);

    public IReadOnlyList<RecordedAddin> All()
    {
        if (!File.Exists(FilePath))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Document>(File.ReadAllText(FilePath), _json) is { SchemaVersion: SchemaVersion } document
                ? document.Registrations.Any(r => r is null) ? throw new JsonException("a registration is null") : document.Registrations
                : throw new JsonException($"schemaVersion is not {SchemaVersion}");
        }
        catch (JsonException ex)
        {
            throw new AddinException(AddinProblem.InvalidRecord, $"{FilePath}: {ex.Message}", ex);
        }
    }

    /// <summary>Records <paramref name="addin"/>, in place of an earlier record for the same EPLAN.exe and variant.</summary>
    public void Add(RecordedAddin addin) => Save([.. All().Where(r => !Same(r, addin.Eplan, addin.Variant)), addin]);

    public void Remove(string eplan, string variant) => Save([.. All().Where(r => !Same(r, eplan, variant))]);

    private static bool Same(RecordedAddin recorded, string eplan, string variant) =>
        string.Equals(recorded.Eplan, eplan, StringComparison.OrdinalIgnoreCase) && string.Equals(recorded.Variant, variant, StringComparison.OrdinalIgnoreCase);

    private void Save(IReadOnlyList<RecordedAddin> registrations)
    {
        Directory.CreateDirectory(stateDirectory);
        string temporary = $"{FilePath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new Document(SchemaVersion, registrations), _json));
        File.Move(temporary, FilePath, overwrite: true);
    }

    private sealed record Document(int SchemaVersion, IReadOnlyList<RecordedAddin> Registrations);
}
