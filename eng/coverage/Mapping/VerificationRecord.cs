using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace EplanMcp.Coverage.Mapping;

internal sealed class VerificationRecord
{
    [YamlIgnore]
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public string Outcome { get; set; } = "";
    public bool Changed { get; set; }
    [JsonPropertyName("undo_restores"), YamlMember(Alias = "undo_restores", ApplyNamingConventions = false)]
    public bool? UndoRestores { get; set; }
    public double Seconds { get; set; }
    [JsonPropertyName("eplan_messages"), YamlMember(Alias = "eplan_messages", ApplyNamingConventions = false)]
    public int EplanMessages { get; set; }
    public string Date { get; set; } = "";
    public string Eplan { get; set; } = "";
    [YamlIgnore]
    public string? Detail { get; set; }
    [JsonIgnore, YamlIgnore]
    public bool Verifies => Outcome == "ok" && UndoRestores != false && (Kind == "api-read" ? !Changed : UndoRestores == true || !Changed && Kind == "action");
}

internal sealed class VerificationRun
{
    [JsonPropertyName("stopped_at")]
    public string? StoppedAt { get; set; }
    [JsonPropertyName("stop_reason")]
    public string? StopReason { get; set; }
    public string Date { get; set; } = "";
    public string Eplan { get; set; } = "";
    public List<VerificationRecord> Entries { get; set; } = [];
}
