using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

[Trait("Size", "Small")]
public sealed class ObjectReadOperationTests
{
    private const string Project = "X:/fixture/Line1";
    internal static DrawingLocator Locator(long oid = 1) => new() { Project = Project, Page = "=A/1", Type = 1, Oid = oid, Digest = new string('a', 64) };
    private static ObjectReadData Execute(Reader reader, ObjectReadArgs args, bool get = false) =>
        (ObjectReadData)((IOperation)new ObjectReadOperation(new FakeCatalog(new OpenProject { Name = "Line1", Path = Project, Current = true }), reader, get ? ObjectReadKind.Get : ObjectReadKind.Find)).Execute(args)!;

    [Fact]
    public void All68Types_HaveAnExplicitStatus_UnsupportedNeverAccessesNativeResources()
    {
        string[] types = [.. PropertyCatalog.Entries.Where(e => !e.Obsolete).Select(e => e.ObjectType).Distinct(StringComparer.Ordinal)];
        Assert.Equal(68, types.Length);
        foreach (string type in types)
        {
            var reader = new Reader();
            ObjectReadData result = Execute(reader, new() { ObjectType = type });
            Assert.Equal(type is "Page" or "Function" or "Connection" or "TerminalStrip" or "Cable" or "PlaceHolder" or "LocationBox"
                or "InterruptionPoint" or "PotentialDefinition" or "MacroBox", result.Unsupported is null);
            if (result.Unsupported is not null)
            {
                Assert.False(string.IsNullOrWhiteSpace(result.Unsupported));
                Assert.Equal(0, reader.Finds);
                Assert.Equal(0, reader.Native.Opens);
                Assert.NotNull(Execute(reader, new() { ObjectType = type, Locator = Locator() }, get: true).Unsupported);
            }
        }
    }

    public static TheoryData<string, string, int> AddedTypes => new()
    {
        { "Terminal", "Function", 22022 }, { "TerminalStrip", "TerminalStrip", 1320 }, { "PLC", "Function", 22022 },
        { "Cable", "Cable", 35108 }, { "PlaceHolder", "PlaceHolder", 19308 }, { "LocationBox", "LocationBox", 25102 },
        { "InterruptionPoint", "InterruptionPoint", 25102 }, { "PotentialDefinition", "PotentialDefinition", 31005 },
        { "BlackBox", "Function", 22022 }, { "PLCTerminal", "Function", 22022 }, { "MacroBox", "MacroBox", 20111 },
    };

    [Theory]
    [MemberData(nameof(AddedTypes))]
    public void AddedType_ReadsItsDictionaryAndReleasesEachScope_AndRejectsStaleObjects(string type, string dictionary, int propertyId)
    {
        var reader = new Reader();
        reader.Objects.Add(new() { Locator = Locator(), Name = "selected" });
        reader.Native.ObjectType = dictionary;
        reader.Native.Properties.Add(propertyId, new() { Stored = "nonempty" });
        int? refused = PropertyCatalog.Entries.Where(e => e.ObjectType == dictionary && e.PropertyId.HasValue && e.ReadRefusal is not null).Select(e => e.PropertyId).FirstOrDefault();
        if (refused.HasValue)
        { reader.Native.Properties.Add(refused.Value, new() { Stored = "must not be read" }); }
        var args = new ObjectReadArgs { ObjectType = type, Locator = Locator(), Language = "en_US" };
        Assert.Single(Execute(reader, args).Objects);
        var rows = new List<PropertyReadRow>();
        int blocks = 0;
        do
        {
            ObjectReadData block = Execute(reader, args, get: true);
            Assert.Null(block.Unsupported);
            rows.AddRange(block.Properties!.Items);
            Assert.InRange(++blocks, 1, 100);
            if (block.NextOffset is not { } next)
            { break; }
            args.Offset = next;
            args.IndexOffset = block.Properties.NextIndexOffset;
        } while (true);
        Assert.Equal("nonempty", Assert.Single(rows, r => r.PropertyId == propertyId).Value!.Text);
        if (refused.HasValue)
        { Assert.Equal("read_refused", Assert.Single(rows, r => r.PropertyId == refused).Status); }
        Assert.DoesNotContain(reader.Native.ValueRequests, request => request.Id == refused);
        Assert.Equal(type.ToLowerInvariant(), reader.Native.Target!.Locator!.Kind);
        Assert.Equal(blocks, reader.Native.Opens);
        reader.Native.AssertDisposed();
        reader.Objects.Clear();
        Assert.Equal(ErrorReason.PlanStale, Assert.Throws<BridgeErrorException>(() => Execute(reader, args, true)).Error.Reason);
        Assert.Equal(blocks, reader.Native.Opens);
    }

    [Theory]
    [MemberData(nameof(AddedTypes))]
    public void AddedType_ValueGetterFails_ReleasesListDefinitionAndValue(string type, string dictionary, int propertyId)
    {
        var reader = new Reader();
        reader.Objects.Add(new() { Locator = Locator() });
        reader.Native.ObjectType = dictionary;
        reader.Native.Properties.Add(propertyId, new());
        reader.Native.OnAccess = stage => { if (stage == "stored") { throw new InvalidOperationException("read failed"); } };
        var args = new ObjectReadArgs
        {
            ObjectType = type,
            Locator = Locator(),
            Offset = PropertyCatalog.Entries.Where(e => e.ObjectType == dictionary && e.PropertyId.HasValue)
                .OrderBy(e => e.PropertyId).Select(e => e.PropertyId).ToList().IndexOf(propertyId),
        };
        Assert.Throws<InvalidOperationException>(() => Execute(reader, args, true));
        Assert.Equal(1, reader.Native.Opens);
        reader.Native.AssertDisposed();
    }

    [Fact]
    public void Find_UsesResolvedProjectAndFilters_AndReturnsBoundedBlocks()
    {
        var reader = new Reader();
        reader.Objects.AddRange(Enumerable.Range(1, 203).Select(i => new ObjectSummary { Locator = Locator(i), Name = "object" }));
        var args = new ObjectReadArgs { Project = "Line1", ObjectType = "Function", Page = "=A/1", NameContains = "name" };
        ObjectReadData first = Execute(reader, args);
        Assert.Equal(200, first.Objects.Count);
        Assert.Equal(200, first.NextOffset);
        Assert.Equal((Project, "=A/1", "name"), (reader.Last!.Project, reader.Last.Page, reader.Last.NameContains));
        args.Offset = first.NextOffset!.Value;
        ObjectReadData last = Execute(reader, args);
        Assert.Equal(new long[] { 201, 202, 203 }, last.Objects.Select(o => o.Locator.Oid));
        Assert.Null(last.NextOffset);
        Assert.Equal(0, reader.Native.Opens);
    }

    [Theory]
    [InlineData("project")]
    [InlineData("page")]
    [InlineData("type")]
    [InlineData("oid")]
    [InlineData("digest")]
    public void Get_StaleIdentityNeverOpensProperties(string changed)
    {
        var reader = new Reader();
        reader.Objects.Add(new() { Locator = Locator() });
        DrawingLocator supplied = Locator();
        switch (changed)
        {
            case "project":
                supplied.Project = "other";
                break;
            case "page":
                supplied.Page = "=A/2";
                break;
            case "type":
                supplied.Type++;
                break;
            case "oid":
                supplied.Oid++;
                break;
            case "digest":
                supplied.Digest = new string('b', 64);
                break;
        }
        BridgeErrorException ex = Assert.Throws<BridgeErrorException>(() => Execute(reader, new() { ObjectType = "Page", Locator = supplied }, true));
        Assert.Equal(ErrorReason.PlanStale, ex.Error.Reason);
        Assert.Equal(0, reader.Native.Opens);
    }

    [Fact]
    public void Get_SparseIndexesLanguageAndRefusal_ReuseOneListPerBlockAndReleaseAllLeases()
    {
        var reader = new Reader();
        reader.Objects.Add(new() { Locator = Locator() });
        reader.Native.ObjectType = "Page";
        var property = new FakeProperty { Metadata = new() { ValueType = "multi_language", IsIndexed = true, IsInternal = false }, Indexes = Enumerable.Range(0, 205).Select(i => i * 2).ToArray() };
        property.Texts.Add("zh_CN", "selected text");
        property.Texts.Add("en_US", "other text");
        reader.Native.Properties.Add(11011, property);
        var args = new ObjectReadArgs { ObjectType = "Page", Locator = Locator(), Language = "zh_CN", Detailed = true };
        var rows = new List<PropertyReadRow>();
        int pages = 0;
        do
        {
            ObjectReadData block = Execute(reader, args, get: true);
            Assert.InRange(block.Properties!.Items.Count, 1, 200);
            rows.AddRange(block.Properties.Items);
            if (block.NextOffset is not { } next)
            { break; }
            Assert.True(next > args.Offset || next == args.Offset && block.Properties.NextIndexOffset > args.IndexOffset);
            args.Offset = next;
            args.IndexOffset = block.Properties.NextIndexOffset;
            Assert.InRange(++pages, 1, 20);
        } while (true);
        Assert.Equal(Enumerable.Range(0, 205).Select(i => (int?)(i * 2)), rows.Where(r => r.PropertyId == 11011).Select(r => r.Index));
        Assert.All(rows.Where(r => r.PropertyId == 11011), r => Assert.Equal(("value", "zh_CN", "selected text"), (r.Status, r.Language, r.Value!.Text)));
        Assert.Contains(rows, r => r.Status == "read_refused" && r.PropertyId == 3010);
        Assert.Equal(pages + 1, reader.Native.Opens);
        reader.Native.AssertDisposed();
    }

    [Theory]
    [InlineData("{\"objectType\":\"Page\",\"offset\":\"1\"}")]
    [InlineData("{\"objectType\":\"Page\",\"write\":true}")]
    [InlineData("[]")]
    public void Parse_RejectsUnknownFieldsAndCoercion(string json)
    {
        var operation = new ObjectReadOperation(new FakeCatalog(), new Reader(), ObjectReadKind.Find);
        Assert.Equal(ErrorReason.InvalidArgument, Assert.Throws<BridgeErrorException>(() => operation.ParseArgs(JToken.Parse(json))).Error.Reason);
    }

    [Fact]
    public void Get_ExceptionInsideRead_DisposesTheSingleList()
    {
        var reader = new Reader();
        reader.Objects.Add(new() { Locator = Locator() });
        reader.Native.ObjectType = "Page";
        reader.Native.Properties.Add(11011, new());
        reader.Native.OnAccess = stage => { if (stage == "stored") { throw new InvalidOperationException("read failed"); } };
        Assert.Throws<InvalidOperationException>(() => Execute(reader, new() { ObjectType = "Page", Locator = Locator() }, true));
        reader.Native.AssertDisposed();
    }

    private sealed class Reader : IObjectReadAdapter
    {
        public FakePropertyReadAdapter Native { get; } = new();
        public List<ObjectSummary> Objects { get; } = [];
        public int Finds { get; private set; }
        public ObjectReadArgs? Last { get; private set; }
        public IReadOnlyList<ObjectSummary> Find(ObjectReadArgs args) { Finds++; Last = args; return Objects; }
        public IPropertyReadScope Open(string projectPath, PropertyTarget target) => Native.Open(projectPath, target);
    }
}
