using System.Text.Json;
using EplanMcp.Packs.Knowledge;

namespace EplanMcp.Packs.Tests;

internal static class ConfigFixtures
{
    public const string SchemaPath = "knowledge/config_schema/motors.json";
    public const string CompanyPath = "knowledge/config_layer/company.json";
    public const string CabinetTypePath = "knowledge/config_layer/cabinet-type.json";

    public static byte[] Read(string path)
    {
        using Stream stream = typeof(ConfigFixtures).Assembly.GetManifestResourceStream("EplanMcp.Packs.Tests.Fixtures." + path.Replace('/', '.'))!;
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }

    public static KnowledgeDocument Document(string path) => KnowledgeDocument.Read(Read("MotorsPack/" + path));

    public static JsonElement Spec(string path) => JsonSerializer.SerializeToElement(Document(path).Spec, PackJson.Options);

    public static JsonElement Schema => Spec(SchemaPath);
}
