using System.Text.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace EplanMcp.Contracts.Tests;

/// <summary>
/// The Host serializes contracts with System.Text.Json and the Bridge with Newtonsoft.Json (ADR-0003). Every DTO must
/// survive a trip through one serializer and back through the other, in both directions, with camelCase names.
/// </summary>
[Trait("Size", "Small")]
public sealed class SerializerCompatibilityTests
{
    private static readonly JsonSerializerOptions _stj = new(JsonSerializerDefaults.Web);

    private static readonly JsonSerializerSettings _newtonsoft = new()
    {
        ContractResolver = new CamelCasePropertyNamesContractResolver(),
    };

    public static TheoryData<object> Samples() =>
    [
        new HandshakeRequest { ProtocolVersion = 1, MinProtocolVersion = 1, HostVersion = "0.1.0" },
        new HandshakeResult
        {
            ProtocolVersion = 1,
            BridgeVersion = "0.1.0",
            Eplan = new EplanInstanceInfo { ProcessId = 4242, Version = "2026.0.3.25421", Variant = "Pro Panel", ApiExtensionLicence = true },
        },
        new HandshakeResult { ProtocolVersion = 1, BridgeVersion = "0.1.0", Eplan = new EplanInstanceInfo { ProcessId = 1, Version = "x" } },
        new HandshakeResult { ProtocolVersion = 1, BridgeVersion = "0.2.0", Eplan = new EplanInstanceInfo { ProcessId = 1 }, Operations = ["page.list", "project.list"] },
        new BridgeError
        {
            Reason = ErrorReason.EplanBusy,
            Message = "EPLAN is busy.",
            Retryable = true,
            Metadata = new Dictionary<string, string> { ["waitedSeconds"] = "10" },
        },
        new BridgeError { Reason = ErrorReason.Internal, Message = "m" },
        new ProjectListArgs(),
        new ProjectListData
        {
            Projects =
            [
                new OpenProject { Name = "Line1", Path = @"D:\Projects\Line1", Current = true },
                new OpenProject { Name = "项目 二", Path = @"D:\项目\二", ReadOnly = true },
            ],
        },
        new ProjectListData(),
        new ListData<OpenProject> { Project = "项目 二", Items = [new OpenProject { Name = "Line1", Path = "p" }], Total = 7 },
        new ListData<OpenProject>(),
        new ProjectGetArgs { Project = @"D:\项目\二", Language = "zh_CN", IncludeProperties = true },
        new ProjectGetArgs(),
        new ProjectGetData
        {
            Project = new OpenProject { Name = "Line1", Path = @"D:\Projects\Line1", Current = true, ReadOnly = true },
            Pages = 321,
            Devices = 420,
            Connections = 2060,
            PartReferences = 2900,
            PagesByType = [new PageTypeCount { Type = "Circuit", Count = 140 }, new PageTypeCount { Type = "TitlePage", Count = 3 }],
            Properties =
            [
                new PropertyEntry { Id = 10000, Name = "项目名称", Value = "Line1" },
                new PropertyEntry { Id = 10901, Index = 2, Value = "多行\n文本" },
            ],
        },
        new ProjectGetData(),
        new PageListArgs { Project = "Line1", NamePrefix = "=GAA+A1&EFS1/", PageType = "Circuit", Text = "电源", Language = "zh_CN", Offset = 50, Limit = 50 },
        new PageListArgs(),
        new ListData<PageSummary>
        {
            Project = "Line1",
            Items =
            [
                new PageSummary { Name = "=GAA+A1&EFS1/1", Type = "Circuit", Description = "电源\n24 V" },
                new PageSummary { Name = "&AAA1/1", Type = "TitlePage" },
            ],
            Total = 140,
        },
        new PageGetArgs { Project = "Line1", Page = "=GAA+A1&EFS1/1", Language = "en_US", IncludeProperties = true },
        new PageGetArgs(),
        new PageGetData
        {
            Project = "项目 二",
            Page = new PageDetail { Name = "&AAA1/1", Type = "TitlePage", TypeName = "标题页/封页", Description = "封页" },
            Devices =
            [
                new PageDevice { Tag = "=GAA+A1-FC1", VisibleTag = "-FC1", Category = "CircuitBreaker" },
                new PageDevice { Tag = "=GAA+A1-TA1", Category = "Transformer" },
            ],
            Properties = [new PropertyEntry { Id = 11011, Name = "页描述", Value = "封页" }],
        },
        new PageGetData(),
        new DeviceListArgs { Project = "Line1", TagContains = "-K1", Page = "=A1&EFS1/1", Category = "Coil", HasPart = false, IncludeAuxiliary = true, Offset = 50, Limit = 50 },
        new DeviceListArgs(),
        new ListData<DeviceListItem>
        {
            Project = "项目 二",
            Items =
            [
                new DeviceListItem { Tag = "=GAA+A1-FC1", VisibleTag = "-FC1", Main = true, Category = "CircuitBreaker", Page = "=GAA+A1&EFS1/1", Parts = 2 },
                new DeviceListItem { Tag = "+A1-FC2:1", Category = "Terminal" },
            ],
            Total = 2876,
        },
        new DeviceGetArgs { Project = @"D:\Projects\Line1", Tag = "-KF1", Language = "zh_CN", Detailed = true },
        new DeviceGetArgs(),
        new DeviceGetData
        {
            Project = "Line1",
            Device = new DeviceFunction { Tag = "=F+A2-KF1", VisibleTag = "-KF1", Category = "Coil", Page = "=F+A2&EFS1/1", Text = "功能文本", Pins = ["A1", "A2"] },
            Functions = [new DeviceFunction { Tag = "=F+A2-KF1", Category = "NoContact", Pins = ["13", "14"] }],
            FunctionCount = 3,
            Parts = [new DevicePart { PartNumber = "SIE.3RT2016-1BB41", Variant = "1", Count = 2, Description = "接触器", Manufacturer = "SIE" }, new DevicePart()],
            Properties = [new PropertyEntry { Id = 20011, Name = "Function text", Value = "1,5" }, new PropertyEntry { Name = "UserProp", Value = "x" }],
        },
        new DeviceGetData(),
    ];

    [Theory]
    [MemberData(nameof(Samples))]
    public void SystemTextJsonToNewtonsoft_RoundTrips(object sample)
    {
        string json = System.Text.Json.JsonSerializer.Serialize(sample, sample.GetType(), _stj);
        object? back = JsonConvert.DeserializeObject(json, sample.GetType(), _newtonsoft);

        Assert.Equal(Canonical(sample), Canonical(back!));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void NewtonsoftToSystemTextJson_RoundTrips(object sample)
    {
        string json = JsonConvert.SerializeObject(sample, _newtonsoft);
        object? back = System.Text.Json.JsonSerializer.Deserialize(json, sample.GetType(), _stj);

        Assert.Equal(Canonical(sample), Canonical(back!));
    }

    [Theory]
    [MemberData(nameof(Samples))]
    public void BothSerializers_ProduceTheSamePropertyNames(object sample)
    {
        using JsonDocument fromStj = JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(sample, sample.GetType(), _stj));
        using JsonDocument fromNewtonsoft = JsonDocument.Parse(JsonConvert.SerializeObject(sample, _newtonsoft));

        Assert.Equal(Names(fromStj.RootElement), Names(fromNewtonsoft.RootElement));
    }

    [Fact]
    public void HandshakeResult_UsesCamelCaseOnTheWire()
    {
        string json = JsonConvert.SerializeObject(new HandshakeResult { Eplan = new EplanInstanceInfo { ProcessId = 7 } }, _newtonsoft);

        Assert.Contains("\"protocolVersion\":", json, StringComparison.Ordinal);
        Assert.Contains("\"eplan\":{\"processId\":7", json, StringComparison.Ordinal);
    }

    /// <summary>A serializer-independent rendering used for equality: STJ with fixed options.</summary>
    private static string Canonical(object value) => System.Text.Json.JsonSerializer.Serialize(value, value.GetType(), _stj);

    private static List<string> Names(JsonElement element, string prefix = "")
    {
        var names = new List<string>();
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (System.Text.Json.JsonProperty property in element.EnumerateObject())
            {
                names.Add(prefix + property.Name);
                names.AddRange(Names(property.Value, prefix + property.Name + "."));
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement item in element.EnumerateArray())
            {
                names.AddRange(Names(item, prefix + "[]."));
            }
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }
}
