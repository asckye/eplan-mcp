using System.Text.Json.Nodes;
using EplanMcp.Host.Doctor;

namespace EplanMcp.Host.Tests.Doctor;

/// <summary>The JSON of <c>doctor --format json</c> (architecture 5.2), from fixed checks.</summary>
[Trait("Size", "Small")]
public sealed class DoctorJsonTests
{
    [Fact]
    public void FormatJson_ListsTheChecksInOrderWithTheSubjectOnlyWhereThereIsOne()
    {
        DoctorCheck[] checks =
        [
            new(DoctorCheckIds.HostVersion, DoctorStatus.Info, "eplanmcp 1.2.3", "eplanmcp 1.2.3"),
            new(DoctorCheckIds.EplanRunning, DoctorStatus.Ok, "EPLAN 正在运行，进程 7", "EPLAN is running, process 7", "7"),
            new(DoctorCheckIds.EplanRunning, DoctorStatus.Ok, "EPLAN 正在运行，进程 9", "EPLAN is running, process 9", "9"),
        ];

        JsonObject json = JsonNode.Parse(DoctorCommand.FormatJson("1.2.3", checks))!.AsObject();

        Assert.Equal(["schemaVersion", "hostVersion", "status", "checks"], json.Select(p => p.Key));
        Assert.Equal(1, json["schemaVersion"]!.GetValue<int>());
        Assert.Equal("1.2.3", json["hostVersion"]!.GetValue<string>());
        JsonObject[] items = [.. json["checks"]!.AsArray().Select(c => c!.AsObject())];
        Assert.Equal(["id", "status", "zh", "en"], items[0].Select(p => p.Key));
        Assert.Equal(["id", "subject", "status", "zh", "en"], items[1].Select(p => p.Key));
        Assert.Equal(["host.version", "eplan.running", "eplan.running"], items.Select(c => c["id"]!.GetValue<string>()));
        Assert.Equal(["7", "9"], items[1..].Select(c => c["subject"]!.GetValue<string>()));
        Assert.Equal(("info", "EPLAN 正在运行，进程 7", "EPLAN is running, process 7"), (items[0]["status"]!.GetValue<string>(), items[1]["zh"]!.GetValue<string>(), items[1]["en"]!.GetValue<string>()));
    }

    [Fact]
    public void FormatJson_NamesEveryStatusThereIs()
    {
        // A new status needs a name of its own, which changes the shape (a new schemaVersion).
        Assert.Equal(["ok", "info", "warning", "failure"], Enum.GetValues<DoctorStatus>().Select(s =>
            JsonNode.Parse(DoctorCommand.FormatJson("1.2.3", [new("x", s, "", "")]))!["checks"]![0]!["status"]!.GetValue<string>()));
    }

    [Theory]
    [InlineData("", "ok")]
    [InlineData("Info", "ok")]
    [InlineData("Ok Info", "ok")]
    [InlineData("Info Warning Ok", "warning")]
    [InlineData("Failure Warning Info Ok", "failure")]
    public void FormatJson_TheStatusIsTheWorstCheckCountingInfoAsOk(string statuses, string worst)
    {
        DoctorCheck[] checks = [.. statuses.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(s => new DoctorCheck("x", Enum.Parse<DoctorStatus>(s), "", ""))];

        Assert.Equal(worst, JsonNode.Parse(DoctorCommand.FormatJson("1.2.3", checks))!["status"]!.GetValue<string>());
    }

    [Fact]
    public void FormatJson_IsOneLineWithTheChineseTextUnescaped()
    {
        string json = DoctorCommand.FormatJson("1.2.3", [new("x", DoctorStatus.Ok, "数据目录：D:\\数据", "Data directory: D:\\数据")]);

        Assert.DoesNotContain('\n', json);
        Assert.Contains("\"zh\":\"数据目录：D:\\\\数据\"", json, StringComparison.Ordinal);
    }
}
