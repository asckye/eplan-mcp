using System.Text.Json;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Bridge.Eplan.Adapters;
using EplanMcp.Contracts;
using EplanMcp.Packs;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Bridge.Core.Tests.Operations;

#pragma warning disable CA2000, CA1001 // Fake native objects are borrowed and deliberately throw on Dispose.

[Trait("Size", "Medium")]
public sealed class ModuleInspectionReaderTests
{
    [Fact]
    public void Read_CrossPageClosureAndBindings_PreservesLanguagesIndexesAndActualProperties()
    {
        var fixture = new Fixture();
        JObject evidence = fixture.Read();
        JArray objects = (JArray)evidence["objects"]!;
        Assert.Contains(objects, o => (string?)o["name"] == "+A1/2");
        Assert.Contains(objects, o => (string?)o["kind"] == "connection");
        Assert.Contains((JArray)evidence["references"]!, r => (string?)r["kind"] == "cross_reference" && (string?)r["to"] == "17/12");
        JObject holder = Assert.IsType<JObject>(Assert.Single((JArray)evidence["placeholders"]!));
        Assert.Equal("Text", holder["variables"]![0]!.Value<string>());
        Assert.Equal(2, holder["valueSets"]![0]!["values"]!["Text"]!.Count());
        Assert.Null(holder["valueSets"]![0]!["values"]!["text"]);
        Assert.Contains((JArray)holder["bindings"]!, b => (string?)b["variable"] == "Part" && (int)b["index"]! == 1);
        JObject function = Assert.IsType<JObject>(objects.Single(o => (string?)o["id"] == "17/11"));
        JArray properties = (JArray)function["properties"]!;
        Assert.Contains(properties, p => (int)p["propertyId"]! == 20011 && (string?)p["language"] == "de_DE" && (string?)p["value"] == "Deutsch");
        Assert.Contains(properties, p => (int)p["propertyId"]! == 20093 && p["value"]!.Type == JTokenType.Boolean && p["value"]!.Value<bool>());
        Assert.Contains(properties, p => (int)p["propertyId"]! == 20027 && (string?)p["state"] == "empty" && p["value"] is null);
        Assert.False(evidence["coverage"]!["placeholderBindings"]!.Value<bool>());
        Assert.False(evidence["complete"]!.Value<bool>());
        Assert.Contains((JArray)evidence["gaps"]!, g => g["message"]!.Value<string>()!.Contains("Missing", StringComparison.Ordinal));
        Assert.All(fixture.Owners.Where(o => o.PropertyListReads > 0), o =>
        {
            Assert.Equal(1, o.PropertyListReads);
            Assert.Equal(1, o.StoredProperties.Disposals);
        });
        Assert.All(fixture.Owners, o => Assert.Equal(0, o.Disposals));
        Assert.DoesNotContain("53/", evidence.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Read_CrossReferencesUnavailable_ReturnsGapAndRetainsOtherEvidence()
    {
        var fixture = new Fixture();
        fixture.First.CrossReferenceError = new BaseException();
        JObject evidence = fixture.Read();
        Assert.False(evidence["coverage"]!["crossPageReferences"]!.Value<bool>());
        Assert.False(evidence["complete"]!.Value<bool>());
        Assert.NotEmpty((JArray)evidence["placeholders"]!);
        Assert.Contains((JArray)evidence["gaps"]!, g => ((string?)g["pointer"])?.StartsWith("/objects/", StringComparison.Ordinal) == true);
    }

    [Fact]
    public void Read_PropertyEnumerationUnavailable_ReleasesEveryAcquiredList()
    {
        var fixture = new Fixture();
        fixture.First.StoredProperties.ReadError = new BaseException();
        JObject evidence = fixture.Read();
        Assert.False(evidence["coverage"]!["placeholderBindings"]!.Value<bool>());
        Assert.False(evidence["complete"]!.Value<bool>());
        Assert.All(fixture.Owners.Where(o => o.PropertyListReads > 0), o => Assert.Equal(1, o.StoredProperties.Disposals));
    }

    [Fact]
    public void Read_ActualPropertyFails_ReturnsUnavailableAndKeepsTheBinding()
    {
        var fixture = new Fixture();
        fixture.First.StoredProperties.FailAfterReads = 1;
        JObject evidence = fixture.Read();
        Assert.False(evidence["coverage"]!["properties"]!.Value<bool>());
        JToken function = ((JArray)evidence["objects"]!).Single(o => (string?)o["id"] == "17/11");
        Assert.Contains((JArray)function["properties"]!, p => (int)p["propertyId"]! == 20011 && (string?)p["state"] == "unavailable" && p["value"] is null);
        Assert.Contains((JArray)evidence["placeholders"]![0]!["bindings"]!, b => (string?)b["variable"] == "Text");
        Assert.Equal(1, fixture.First.StoredProperties.Disposals);
    }

    [Fact]
    public void Read_ClosureExceedsFiftyPages_RefusesWithoutClippingCrossReferences()
    {
        Page[] pages = [.. Enumerable.Range(1, 51).Select(i => new Page(i) { Name = "+A1/" + i })];
        Function[] functions = [.. pages.Select((p, i) => new Function(100 + i) { Page = p })];
        for (int i = 0; i < pages.Length; i++)
        { pages[i].AllPlacements = [functions[i]]; }
        functions[0].CrossReferences = functions;
        EplanProjects.Project = new(pages);
        BridgeError error = Assert.Throws<BridgeErrorException>(() => new EplanModuleInspectionReader().Read(new() { Project = "project", Pages = ["+A1/1"] })).Error;
        Assert.Equal(ErrorReason.CapabilityMissing, error.Reason);
    }

    [Fact]
    public void Read_EmptyReadOnlyPage_ProvesSixCoverageFlags()
    {
        EplanProjects.Project = new(new Page());
        ModuleInspectData result = new EplanModuleInspectionReader().Read(new() { Project = "project", Pages = ["+A1/1"] });
        Assert.True(result.Complete);
        Assert.Equal(6, result.Coverage.Count);
        Assert.All(result.Coverage.Values, Assert.True);
        Assert.Empty(result.Gaps);
    }

    [Fact]
    public void Read_WritableProject_IsRejectedBeforeDiscovery()
    {
        EplanProjects.Project = new(new Page()) { IsReadOnly = false };
        BridgeError error = Assert.Throws<BridgeErrorException>(() => new EplanModuleInspectionReader().Read(new() { Project = "project", Pages = ["+A1/1"] })).Error;
        Assert.Equal(ErrorReason.InvalidArgument, error.Reason);
    }

    [Fact]
    public void Read_MissingPage_MarksPageCoverageIncomplete()
    {
        EplanProjects.Project = new(new Page());
        ModuleInspectData result = new EplanModuleInspectionReader().Read(new() { Project = "project", Pages = ["+A1/404"] });
        Assert.False(result.Complete);
        Assert.False(result.Coverage["pages"]);
        Assert.NotEmpty(result.Gaps);
    }

    private sealed class Fixture
    {
        private readonly Project _project;
        public Function First { get; }
        public StorableObject[] Owners { get; }
        public Fixture()
        {
            var firstPage = new Page(1);
            var secondPage = new Page(2) { Name = "+A1/2" };
            First = new(11) { Page = firstPage };
            var second = new Function(12) { Page = secondPage };
            First.CrossReferences = [second];
            var holder = new PlaceHolder(20) { Page = firstPage, AssignedObjects = [First], VariableNames = ["Text", "Enabled", "Part", "Empty", "Missing"] };
            firstPage.AllPlacements = [First, holder];
            secondPage.AllPlacements = [second];
            _project = new(firstPage, secondPage) { Connections = [new(30) { Page = firstPage, StartPin = new(First), EndPin = new(second) }] };
            First.StoredProperties.Values[(20011, 0)] = "en_US@English;de_DE@Deutsch;";
            First.StoredProperties.Values[(20093, 0)] = true;
            First.StoredProperties.Values[(20100, 1)] = "Part-1";
            First.StoredProperties.Values[(20027, 0)] = null;
            holder.Bindings[(First, 20011, 0)] = "??_??@<Text>;";
            holder.Bindings[(First, 20093, 0)] = "??_??@<Enabled>;";
            holder.Bindings[(First, 20100, 1)] = "??_??@<Part>;";
            holder.Bindings[(First, 20027, 0)] = "??_??@<Empty>;";
            foreach (string variable in holder.VariableNames)
            { holder.Values[("Run", variable)] = variable == "Text" ? "en_US@English;de_DE@Deutsch;" : "??_??@Value;"; }
            Owners = [First, second, firstPage, secondPage, holder, _project];
        }
        public JObject Read()
        {
            EplanProjects.Project = _project;
            ModuleInspectData result = new EplanModuleInspectionReader().Read(new() { Project = "project", Pages = ["+A1/1"] });
            result.MacroHash = new string('a', 64);
            result.ApiExtensionLicence = true;
            var evidence = (JObject)BridgeJson.FromObject(result);
            using JsonDocument document = JsonDocument.Parse(evidence.ToString());
            Assert.False(ModuleInspectionReader.Read(document.RootElement, result.MacroHash).Complete);
            return evidence;
        }
    }
}
