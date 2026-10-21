using System.Text.Json;

namespace EplanMcp.Host.ClientConfig;

/// <summary>An entry client-config wrote: the client, its file, the entry's name, and when.</summary>
internal sealed record RecordedEntry(string Client, string File, string Entry, DateTimeOffset Time);

/// <summary>
/// <c>state\clients.json</c>: the entries client-config wrote, one per client, file and entry name, so that
/// <c>client-config --remove</c> without a client (the uninstaller) removes exactly those (M5 design 4.3). Written to a
/// temporary file and moved into place. A record that cannot be read is an error, never an empty list: writing over it
/// would forget entries.
/// </summary>
internal sealed class ClientRecord(string stateDirectory)
{
    public const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    public string FilePath => Path.Combine(stateDirectory, "clients.json");

    public static ClientRecord For(HostDataLocations locations) => new(locations.State);

    public IReadOnlyList<RecordedEntry> All()
    {
        if (!File.Exists(FilePath))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<Document>(File.ReadAllText(FilePath), _json) is { SchemaVersion: SchemaVersion } document
                ? document.Entries.Any(e => e is null) ? throw new JsonException("an entry is null") : document.Entries
                : throw new JsonException($"schemaVersion is not {SchemaVersion}");
        }
        catch (JsonException ex)
        {
            throw new ClientConfigException(ClientProblem.InvalidRecord, $"{FilePath}: {ex.Message}", ex);
        }
    }

    /// <summary>Records <paramref name="entry"/>, in place of an earlier record of the same entry.</summary>
    public void Add(RecordedEntry entry) => Save([.. All().Where(e => !Same(e, entry.Client, entry.File, entry.Entry)), entry]);

    public void Remove(string client, string file, string entry) => Save([.. All().Where(e => !Same(e, client, file, entry))]);

    private static bool Same(RecordedEntry recorded, string client, string file, string entry) =>
        recorded.Client == client && recorded.Entry == entry && string.Equals(recorded.File, file, StringComparison.OrdinalIgnoreCase);

    private void Save(IReadOnlyList<RecordedEntry> entries)
    {
        Directory.CreateDirectory(stateDirectory);
        string temporary = $"{FilePath}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(new Document(SchemaVersion, entries), _json));
        File.Move(temporary, FilePath, overwrite: true);
    }

    private sealed record Document(int SchemaVersion, IReadOnlyList<RecordedEntry> Entries);
}
