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
                new OpenProject { Name = "Line1", Path = @"D:\Projects\Line1.elk", Current = true },
                new OpenProject { Name = "项目 二", Path = @"D:\项目\二.elk", ReadOnly = true },
            ],
        },
        new ProjectListData(),
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
