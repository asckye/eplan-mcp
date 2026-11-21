using System.Security.Cryptography;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace EplanMcp.Workbench.Protocol;

/// <summary>
/// The SHA-256 that binds an approval to a write's exact arguments (ADR-0011), in one place for the Host, which puts it
/// in the question, and the Workbench, which recognises a write it sent itself (ADR-0065 decision 3). The arguments
/// are the tool's typed argument object from <c>EplanMcp.Contracts</c>, serialized with <see cref="Options"/>.
/// </summary>
public static class ArgumentsHash
{
    /// <summary>The Host's JSON settings: web defaults (camelCase) and relaxed escaping, so non-ASCII text stays as it is.</summary>
    public static JsonSerializerOptions Options { get; } = Create();

    /// <summary>The lower-case hex SHA-256 of <paramref name="args"/> serialized with <see cref="Options"/>.</summary>
    public static string Of<TArgs>(TArgs args) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(args, Options)));

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }
}
