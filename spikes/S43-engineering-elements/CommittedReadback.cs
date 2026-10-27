using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Newtonsoft.Json.Linq;

namespace EplanMcp.Spikes.S43;

internal sealed partial class Probe
{
    internal void ValidateCommitted(string step, string group, Action<string, object> log)
    {
        // Field reads are diagnostic and null-safe; business mismatches retain the existing explicit failure rule.
        if (step == "terminals")
        {
            var page = RequireGroupPage("terminals");
            var result = new ReadRecord();
            Placement[]? placements = result.Get<Placement[]?>("allPlacements", () => page.AllPlacements);
            TerminalStrip[]? strips = placements?.OfType<TerminalStrip>().Where(s => result.Get<string?>("strip.name", () => s.Name) == "+S43LAB-S43X").ToArray();
            Terminal[]? terminals = placements?.OfType<Terminal>().OrderBy(t => result.Get<string?>("terminal.name", () => t.Name), StringComparer.Ordinal).ToArray();
            int? members = strips is { Length: 1 } ? result.Get<int?>("strip.terminals", () => strips[0].Terminals?.Length) : null;
            result.Array("rows", () => terminals, terminal =>
            {
                var row = new ReadRecord();
                TerminalStrip? parent = row.Get<TerminalStrip?>("terminalStrip", () => terminal.TerminalStrip);
                row.Field("requestedCategory", () => "Terminal");
                row.Field("actualCategory", () => terminal.FunctionCategory.ToString());
                row.Field("expectedStrip", () => "+S43LAB-S43X");
                row.Field("actualStrip", () => parent?.Name);
                row.Field("actualStripOid", () => parent?.ObjectIdentifier);
                row.Field("Name", () => terminal.Name);
                row.Field("designation", () => Text(terminal, 20030));
                row.Field("ObjectIdentifier", () => terminal.ObjectIdentifier);
                return row.Data;
            });
            var rows = result.Data["rows"] as JArray;
            bool exact = strips?.Length == 1 && terminals?.Length == 3 && members == 3 && rows is not null && rows.Select((r, i) => r is JObject item &&
                (string?)item["actualCategory"] == "Terminal" && (string?)item["actualStrip"] == (string?)item["expectedStrip"] &&
                (string?)item["designation"] == Evidence.Number(i + 1) && (string?)item["Name"] == (string?)item["expectedStrip"] + ":" + Evidence.Number(i + 1)).All(x => x);
            result.Field("exact", () => exact);
            result.Field("stripCount", () => strips?.Length);
            result.Field("terminalCount", () => terminals?.Length);
            result.Field("stripMemberCount", () => members);
            log("committed_terminal_relations", result.Data);
            if (!exact) { throw new InvalidOperationException("Committed terminal membership readback differs; placement has committed, inspect before retrying."); }
        }
        if (step == "plc")
        {
            var page = RequireGroupPage("plc");
            var result = new ReadRecord();
            Placement[]? placements = result.Get<Placement[]?>("allPlacements", () => page.AllPlacements);
            PLC[]? boxes = placements?.OfType<PLC>().Where(p => result.Get<string?>("plc.name", () => p.Name) == Tag("KF")).ToArray();
            Terminal[]? terminals = placements?.OfType<Terminal>().Where(t => result.Get<string?>("terminal.category", () => t.FunctionCategory.ToString()) == "PLCTerminal")
                .OrderBy(t => result.Get<string?>("terminal.address", () => Text(t, PlcFields.Address)), StringComparer.Ordinal).ToArray();
            int? members = boxes is { Length: 1 } ? result.Get<int?>("plc.terminals", () => boxes[0].PLCTerminals?.Length) : null;
            result.Array("rows", () => terminals, terminal =>
            {
                var row = new ReadRecord();
                row.Field("Name", () => terminal.Name);
                row.Field("parent", () => terminal.PLC?.Name);
                row.Field("address", () => Text(terminal, PlcFields.Address));
                row.Field("symbolicAddress", () => Text(terminal, PlcFields.SymbolicManual));
                row.Field("channel", () => Text(terminal, PlcFields.Channel));
                return row.Data;
            });
            var rows = result.Data["rows"] as JArray;
            bool exact = boxes?.Length == 1 && terminals?.Length == 3 && members == 3 && rows is not null && rows.Select((r, i) => r is JObject item &&
                (string?)item["parent"] == Tag("KF") && (string?)item["address"] == "I0." + Evidence.Number(i) &&
                (string?)item["symbolicAddress"] == "S43_DI_" + Evidence.Number(i)).All(x => x);
            result.Field("exact", () => exact);
            log("committed_plc_relations", result.Data);
            if (!exact) { throw new InvalidOperationException("Committed PLC relation/address readback differs; placement has committed, inspect before retrying."); }
        }
        if (step is "cable" or "cable_line")
        {
            string target = step == "cable_line" ? "cable_wires" : "cable";
            log("committed_cable_inventory", CablePageData(RequireGroupPage(target)));
        }
    }
}
