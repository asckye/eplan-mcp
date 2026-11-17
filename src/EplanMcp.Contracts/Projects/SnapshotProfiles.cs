namespace EplanMcp.Contracts;

/// <summary>
/// The snapshot profiles and their registered properties (docs/design/2026-10-21-m6-bridge-snapshot.md, 4.2). Data
/// only. Any change to a set, or to the normalisation, takes a new <see cref="Format"/>: hashes of two formats are never
/// comparable.
/// </summary>
public static class SnapshotProfiles
{
    /// <summary>The format of the canonical text and the hashes; the first line of every hashed text.</summary>
    public const string Format = "eplanmcp.snapshot/1";

    /// <summary>The one profile so far.</summary>
    public const string StandardName = "standard";

    /// <summary>
    /// The registered set of <see cref="StandardName"/>: what facts, rules and diffs need, read live by the existing
    /// adapters unless noted, never personal data (no <c>PROPUSER_*</c>, api-notes A36) and no value that changes when
    /// the project is opened again.
    /// </summary>
    public static readonly IReadOnlyList<SnapshotProperty> Standard =
    [
        new("page", 11011, "PAGE_NOMINATIOMN", SnapshotProperty.MultiLanguage), // description (A12, A36)
        new("page", 11016, "PAGE_FORMPLOT", SnapshotProperty.Text), // plot frame; not read live before
        new("page", 11024, "PAGE_LASTAUTOMODIFICATIONDATE", SnapshotProperty.Hint), // A36
        new("page", 11001, "PAGE_LASTAUTOMODIFICATIONTIME", SnapshotProperty.Hint), // A36
        new("function", 20011, "FUNC_TEXT", SnapshotProperty.MultiLanguage), // A12
        new("function", 20027, "FUNC_TECHNICAL_CHARACTERISTIC", SnapshotProperty.Text), // ratings; not read live before
        new("terminal", 20400, "FUNC_PLCADDRESS", SnapshotProperty.Text), // A24
        new("terminal", 20405, "FUNC_PLCDATATYPE", SnapshotProperty.Text), // A24
        new("terminal", 20403, "FUNC_PLCSYMBOLICADDRESS_CALCULATED", SnapshotProperty.Text), // A24
        new("plc", 20408, "FUNC_PLCSTATIONNAME", SnapshotProperty.Text), // A24
        new("plc", 20410, "FUNC_PLCRACK", SnapshotProperty.Text), // A24
        new("plc", 20411, "FUNC_PLCSOCKET", SnapshotProperty.Text), // A24
        new("cable", 20040, "FUNC_CABLETYPE", SnapshotProperty.Text), // A24
        new("cable", 20042, "FUNC_CABLEWIRECOUNT", SnapshotProperty.Text), // A24
        new("cable", 20043, "FUNC_CABLEWIRECROSSSECTION", SnapshotProperty.Text), // A24
        new("cable", 20257, "FUNC_CABLELENGTH_FULL", SnapshotProperty.Text), // A24
        new("connection", 31011, "CONNECTION_DESIGNATION", SnapshotProperty.Text), // A24
        new("connection", 31002, "CONNECTION_WIRECROSSSECTION", SnapshotProperty.Text), // M2
        new("connection", 31004, "CONNECTION_WIRENUMBER", SnapshotProperty.Text), // M2
    ];
}
