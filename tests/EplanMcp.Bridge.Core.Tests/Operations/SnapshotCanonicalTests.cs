using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

/// <summary>
/// Snapshot objects, pages and hashes (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.4): the same content always gives
/// the same hash, whatever order EPLAN returns things in and whichever database number the opening got; a change shows
/// in exactly the object, page and snapshot it belongs to.
/// </summary>
[Trait("Size", "Small")]
public sealed class SnapshotCanonicalTests
{
    private const string Standard = SnapshotProfiles.StandardName;
    private static readonly IReadOnlyDictionary<int, SnapshotProperty> _registered = SnapshotCanonical.Registered(Standard)!;

    [Fact]
    public void Text_OfAFunction_IsTheDocumentedCanonicalForm()
    {
        SnapshotObject o = SnapshotCanonical.ObjectOf(Function(28, "=+-CU1", "Motor\tM1", x: 120, y: 180.5004), _registered);

        Assert.Equal(
            string.Join(
                "\n",
                "eplanmcp.snapshot/1",
                "kind=function",
                "type=17",
                "oid=59641",
                "name==+-CU1",
                "page==AP+ST1/3",
                "category=Motor",
                "shown=-CU1",
                "main=true",
                "pos=120,180.5",
                "part=SIE.3RV2011-1EA10",
                "p.20011.de_DE=Motor\\tM1",
                "p.20011.zh_CN=电机",
                "p.20400=%QX2.0"),
            SnapshotCanonical.Text(o));
        Assert.Equal(SnapshotCanonical.Sha256(SnapshotCanonical.Text(o)), o.Hash);
        Assert.Equal((17, 59641L, "120,180.5"), (o.Type, o.Oid, o.Pos));
    }

    [Fact]
    public void Object_AnotherDatabaseNumberAndAnotherValueOrder_GiveTheSameHash()
    {
        SnapshotSourceObject first = Function(28, "=+-CU1", "Motor", 120, 180.5);
        SnapshotSourceObject second = Function(53, "=+-CU1", "Motor", 120.0001, 180.4999);
        second.Values.Reverse();

        Assert.Equal(SnapshotCanonical.ObjectOf(first, _registered).Hash, SnapshotCanonical.ObjectOf(second, _registered).Hash);
    }

    [Fact]
    public void Page_ObjectOrderAndHints_DoNotChangeTheHash_ButTheHintIsReturned()
    {
        SnapshotSourceObject page = Page(28, 21, "=AP+ST1/3", "1792643412");
        SnapshotSourceObject[] objects = [Function(28, "=+-CU1", "Motor", 1, 2), Connection(28, 900, "1", "2")];
        SnapshotSourceObject touched = Page(53, 21, "=AP+ST1/3", "1792700000");

        SnapshotPage a = SnapshotCanonical.Page(Standard, page, objects, _registered);
        SnapshotPage b = SnapshotCanonical.Page(Standard, touched, objects.Reverse(), _registered);

        Assert.Equal(a.Hash, b.Hash);
        Assert.Equal(("1792643412 36000", "1792700000 36000"), (a.Modified, b.Modified));
        Assert.Equal(["page", "function", "connection"], a.Objects.Select(o => o.Kind));
        Assert.Equal((4, 21L, "=AP+ST1/3", "Circuit"), (a.Type, a.Oid, a.Name, a.Objects[0].PageType));
    }

    [Fact]
    public void Page_ADescriptionChange_ChangesThatPageAndTheSnapshotOnly()
    {
        SnapshotPage one = SnapshotCanonical.Page(Standard, Page(28, 21, "=AP+ST1/1", "1"), [], _registered);
        SnapshotPage two = SnapshotCanonical.Page(Standard, Page(28, 22, "=AP+ST1/2", "1"), [], _registered);
        SnapshotSourceObject changed = Page(28, 22, "=AP+ST1/2", "2");
        changed.Values[0] = new SnapshotSourceValue(11011, null, "de_DE@Neu;");
        SnapshotPage twoChanged = SnapshotCanonical.Page(Standard, changed, [], _registered);

        Assert.NotEqual(two.Hash, twoChanged.Hash);
        Assert.NotEqual(SnapshotCanonical.SnapshotHash(Standard, [one, two]), SnapshotCanonical.SnapshotHash(Standard, [one, twoChanged]));
        Assert.Equal(SnapshotCanonical.SnapshotHash(Standard, [one, two]), SnapshotCanonical.SnapshotHash(Standard, [two, one]));
    }

    [Fact]
    public void Connection_IsNamedByItsEndsNumbers_SoRenamingADeviceKeepsItsHash()
    {
        SnapshotObject before = SnapshotCanonical.ObjectOf(Connection(28, 900, "13", "A1"), _registered);
        SnapshotSourceObject renamed = Connection(53, 900, "13", "A1");
        renamed.Start!.Function = "=+-QA9";
        SnapshotObject after = SnapshotCanonical.ObjectOf(renamed, _registered);

        Assert.Equal("17/59641:13>17/59700:A1", before.Name);
        Assert.Equal(before.Hash, after.Hash);
        Assert.Equal(("=+-QA9", "=AP+ST1/3"), (after.Start!.Function, after.Page));
        Assert.DoesNotContain("name=", SnapshotCanonical.Text(before), StringComparison.Ordinal);
    }

    [Fact]
    public void Connection_AnEndThatIsNotConnected_IsADash()
    {
        SnapshotSourceObject open = Connection(28, 901, "13", "A1");
        open.End = null;

        Assert.Equal("17/59641:13>-", SnapshotCanonical.ObjectOf(open, _registered).Name);
    }

    [Theory]
    [InlineData("28/17/59641")]
    [InlineData("x/17/59641/0")]
    [InlineData("28/x/59641/0")]
    public void TypeOf_AnIdentifierOfAnotherForm_IsInternal(string identifier) =>
        Assert.Equal(ErrorReason.Internal, Assert.Throws<BridgeErrorException>(() => SnapshotCanonical.TypeOf(identifier)).Error.Reason);

    [Fact]
    public void Registered_AnUnknownProfile_IsNull() => Assert.Null(SnapshotCanonical.Registered("full"));

    private static SnapshotSourceObject Function(int database, string name, string text, double x, double y) => new()
    {
        Kind = "function",
        Identifier = database + "/17/59641/0",
        Oid = 59641,
        Name = name,
        Page = "=AP+ST1/3",
        Category = "Motor",
        Shown = "-CU1",
        Main = true,
        X = x,
        Y = y,
        Parts = ["SIE.3RV2011-1EA10"],
        Values =
        [
            new SnapshotSourceValue(20011, null, "zh_CN@电机;de_DE@" + text + ";"),
            new SnapshotSourceValue(20400, null, "%QX2.0"),
            new SnapshotSourceValue(3010, null, "EPL"),
        ],
    };

    private static SnapshotSourceObject Page(int database, long oid, string name, string date) => new()
    {
        Kind = "page",
        Identifier = database + "/4/" + oid + "/0",
        Oid = oid,
        Name = name,
        PageType = "Circuit",
        Values =
        [
            new SnapshotSourceValue(11011, null, "de_DE@Steuerung;"),
            new SnapshotSourceValue(SnapshotCanonical.PageModifiedDate, null, date),
            new SnapshotSourceValue(SnapshotCanonical.PageModifiedTime, null, "36000"),
        ],
    };

    private static SnapshotSourceObject Connection(int database, long oid, string startPin, string endPin) => new()
    {
        Kind = "connection",
        Identifier = database + "/30/" + oid + "/0",
        Oid = oid,
        Page = "=AP+ST1/3",
        Start = new SnapshotSourceEnd { Identifier = database + "/17/59641/0", Oid = 59641, Function = "=+-CU1", Pin = startPin },
        End = new SnapshotSourceEnd { Identifier = database + "/17/59700/0", Oid = 59700, Function = "=+-QA1", Pin = endPin },
        Values = [new SnapshotSourceValue(31011, null, "433")],
    };
}
