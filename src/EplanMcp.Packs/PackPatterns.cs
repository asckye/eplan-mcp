namespace EplanMcp.Packs;

/// <summary>The patterns of the manifest's strings: the schema declares them and the validator applies the same text.</summary>
public static class PackPatterns
{
    /// <summary>A pack or scope id, such as <c>acme.cabinet-base</c>; at most 64 characters (validator).</summary>
    public const string Id = "^[a-z0-9]+(?:[.-][a-z0-9]+)*$";

    /// <summary>SemVer 2.0.0, the pattern semver.org gives.</summary>
    public const string SemVer =
        @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-((?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*)(?:\.(?:0|[1-9]\d*|\d*[a-zA-Z-][0-9a-zA-Z-]*))*))?(?:\+([0-9a-zA-Z-]+(?:\.[0-9a-zA-Z-]+)*))?$";

    public const string Sha256 = "^[0-9a-f]{64}$";

    /// <summary>One to four numbers, such as <c>2026.0.3.25421</c> or <c>2.7</c>.</summary>
    public const string EplanVersion = @"^\d{1,9}(?:\.\d{1,9}){0,3}$";

    /// <summary>
    /// A requirement by prefix (M6 design 4.4): <c>A:&lt;action&gt;[/TYPE]</c>, <c>O:&lt;type&gt;.&lt;name&gt;</c> (a catalog
    /// overload group, never a member id with parameters), <c>op:&lt;Bridge operation&gt;</c>, <c>licence:&lt;LicenseOptions
    /// name&gt;</c>, <c>plugin:&lt;id&gt;</c>, or a tool name <c>eplan_*</c>; at most 256 characters (validator).
    /// </summary>
    public const string Capability =
        "^(?:A:[A-Za-z][A-Za-z0-9]*(?:/[A-Za-z][A-Za-z0-9]*)?|O:[A-Za-z_][A-Za-z0-9_]*(?:\\.[A-Za-z_#][A-Za-z0-9_`#]*)+|op:[a-z][A-Za-z0-9_]*(?:\\.[a-z][A-Za-z0-9_]*)*"
        + "|licence:[A-Za-z][A-Za-z0-9_]*|plugin:[a-z0-9]+(?:[.-][a-z0-9]+)*|eplan_[a-z0-9_]+)$";

    /// <summary>A snake_case name: fact names, subtypes.</summary>
    public const string Name = "^[a-z][a-z0-9_]{0,63}$";
}
