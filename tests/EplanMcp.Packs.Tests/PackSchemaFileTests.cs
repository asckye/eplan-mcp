using System.Diagnostics;
using System.Text;

namespace EplanMcp.Packs.Tests;

/// <summary>
/// The committed schema is the generated one, and an independent validator (PowerShell's Test-Json, the one CI uses for
/// the action catalog) agrees with EplanMcp on what is valid.
/// </summary>
[Trait("Size", "Medium")]
public sealed class PackSchemaFileTests : IDisposable
{
    private readonly DirectoryInfo _directory = Directory.CreateTempSubdirectory("eplanmcp-schema-");

    [Theory]
    [InlineData("pack-manifest")]
    [InlineData("pack-lock")]
    [InlineData("project-binding")]
    public void CommittedSchema_IsTheGeneratedOne(string name)
    {
        string file = Path.Combine(Repository.Root, "schemas", name + ".schema.json");
        string expected = PackSchema.Text(name switch { "pack-lock" => PackSchema.Lock(), "project-binding" => PackSchema.Binding(), _ => PackSchema.Manifest() });
        string committed = File.Exists(file) ? File.ReadAllText(file, Encoding.UTF8) : string.Empty;

        if (committed != expected)
        {
            // Accepting a schema change is a reviewed step: compare, then move the received file over the committed one.
            File.WriteAllText(Path.ChangeExtension(file, ".received.json"), expected, new UTF8Encoding(false));
            Assert.Fail($"{file} differs from the schema the types give; see {name}.schema.received.json beside it.");
        }
    }

    [Fact]
    public async Task TestJson_AgreesWithTheValidator()
    {
        string valid = Write("valid.json", PackJson.Serialize(Manifests.Complete()));
        string invalid = Write("invalid.json", Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(PackJson.Serialize(Manifests.Complete()))
            .Replace("\"kind\": \"macro\"", "\"kind\": \"script\"", StringComparison.Ordinal)));
        string schema = Write("schema.json", Encoding.UTF8.GetBytes(PackSchema.Text(PackSchema.Manifest())));

        // Test-Json prints True or False; for False it may also write the violations to stderr (not on every runner).
        (string output, string error) = await PowerShellAsync(
            $"foreach ($f in '{valid}', '{invalid}') {{ Test-Json -Path $f -SchemaFile '{schema}' }}");

        Assert.True(
            output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is ["True", "False"],
            $"stdout: {output}{Environment.NewLine}stderr: {error}");
    }

    public void Dispose() => _directory.Delete(recursive: true);

    private string Write(string name, byte[] content)
    {
        string path = Path.Combine(_directory.FullName, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static async Task<(string Output, string Error)> PowerShellAsync(string command)
    {
        var start = new ProcessStartInfo("pwsh", ["-NoProfile", "-NonInteractive", "-Command", command])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        Process process;
        try
        {
            process = Process.Start(start)!;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Assert.Skip("PowerShell 7 (pwsh) is not installed; CI has it.");
            throw;
        }

        using (process)
        {
            Task<string> output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            Task<string> error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            await process.WaitForExitAsync(TestContext.Current.CancellationToken);
            return (await output, await error);
        }
    }
}
