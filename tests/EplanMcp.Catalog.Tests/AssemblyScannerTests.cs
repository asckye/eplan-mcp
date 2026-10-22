using System.Xml.Linq;

namespace EplanMcp.Catalog.Tests;

/// <summary>
/// Scans this test assembly's own metadata and compares the result with the documentation file the C# compiler wrote
/// for it: the compiler is the reference spelling of documentation IDs. Reads two files from disk, hence Medium.
/// </summary>
[Trait("Size", "Medium")]
public sealed class AssemblyScannerTests
{
    internal const string Prefix = "EplanMcp.Catalog.Tests.Fixture";
    private const string W = "EplanMcp.Catalog.Tests.Fixture.Widget";

    private static readonly string _assembly = typeof(AssemblyScannerTests).Assembly.Location;
    private static readonly ScannedAssembly _scanned = AssemblyScanner.Scan(_assembly, Prefix);
    private static readonly HashSet<string> _compilerIds = [.. XDocument.Load(Path.ChangeExtension(_assembly, ".xml"))
        .Descendants("member").Select(m => (string)m.Attribute("name")!)];

    [Fact]
    public void Scan_EveryPublicEntry_HasTheCompilersDocumentationId()
    {
        // The compiler documents no delegate members; every other fixture member has a doc comment.
        var undocumented = _scanned.Entries
            .Where(e => e.Kind != "delegate_method" && e.DeclaringType != "T:" + Prefix + ".ShapeHandler")
            .Select(e => e.Id).Where(id => !_compilerIds.Contains(id)).ToList();

        Assert.Empty(undocumented);
        Assert.True(_scanned.Entries.Count > 40, $"only {_scanned.Entries.Count} entries scanned");
    }

    [Fact]
    public void Scan_EveryCompilerIdOfTheFixture_IsPublicOrKnownToBeNonPublic()
    {
        var known = _scanned.Entries.Select(e => e.Id).Concat(_scanned.NonPublicIds).ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain(_compilerIds, id => id[2..].StartsWith(Prefix + ".", StringComparison.Ordinal) && !known.Contains(id));
    }

    [Fact]
    public void Scan_LeavesOutWhatIsNotPublicApi()
    {
        string[] hidden = [$"M:{W}.Hidden", $"T:{W}.Secret", $"M:{W}.Secret.Hide", $"T:{Prefix}.InternalType",
            $"M:{W}.EplanMcp#Catalog#Tests#Fixture#IShape#Draw"];

        Assert.All(hidden, id => Assert.DoesNotContain(_scanned.Entries, e => e.Id == id));
        Assert.All(hidden, id => Assert.Contains(id, _scanned.NonPublicIds));
    }

    [Fact]
    public void Scan_RecordsKindsAccessAndSignatures()
    {
        Assert.Equal(("method", (string?)null), (Get($"M:{W}.Move(System.Double@,System.Double@)").Kind, Get($"M:{W}.Move(System.Double@,System.Double@)").Access));
        Assert.Equal("protected", Get($"M:{W}.OnChanged").Access);
        Assert.Equal("get;set", Get($"P:{W}.Name").Accessors);
        Assert.Equal("get", Get($"P:{W}.Count").Accessors);
        Assert.Equal(("constant", "3"), (Get($"F:{W}.Limit").Kind, Get($"F:{W}.Limit").Value));
        Assert.Equal(("enum_value", "2"), (Get($"F:{Prefix}.Shade.Dark").Kind, Get($"F:{Prefix}.Shade.Dark").Value));
        Assert.Equal("operator", Get($"M:{W}.op_Implicit({W})~System.String").Kind);
        Assert.Equal("event", Get($"E:{W}.Changed").Kind);
        Assert.Equal("delegate_method", Get($"M:{Prefix}.ShapeHandler.Invoke({Prefix}.IShape)").Kind);
        Assert.True(Get($"M:{W}.Sum(System.Int32[0:,0:])").Static);
        Assert.Equal([new Parameter("name", "System.String"), new Parameter("sizes", "System.Int32[]")],
            Get($"M:{W}.#ctor(System.String,System.Int32[])").Parameters!);
        Assert.Equal($"T:{W}", Get($"T:{W}.Part").DeclaringType);
        Assert.Equal(("class", "static"), (Get($"T:{Prefix}.Properties").Kind, Get($"T:{Prefix}.Properties").Modifiers));
        Assert.Equal(["EplanMcp.Catalog.Tests.Fixture.IShape"], Get($"T:{W}").Interfaces!);
        Assert.True(Get($"T:{Prefix}.Legacy").Obsolete);
    }

    private static CatalogEntry Get(string id) => _scanned.Entries.Single(e => e.Id == id);
}
