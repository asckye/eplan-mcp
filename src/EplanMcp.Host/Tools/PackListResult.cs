using System.ComponentModel;
using System.Text.Json.Serialization;

namespace EplanMcp.Host.Tools;

/// <summary>Output of <c>eplan_pack_list</c> (docs/tools/eplan_pack_list.md, section 3).</summary>
internal sealed class PackListResult : IPagedResult<PackListResult.PackEntry>
{
    [Description("Packs on this page of results, by id.")]
    public List<PackEntry> Packs { get; set; } = [];

    [Description("Number of matching packs, on all result pages.")]
    public int Total { get; set; }

    [Description("Pass as 'cursor' to get the next page; null on the last page.")]
    public string? NextCursor { get; set; }

    [Description("True when entries were cut to stay within the size budget.")]
    public bool Truncated { get; set; }

    [Description("What to do next when entries were cut or bindings could not be read; null otherwise.")]
    public string? Hint { get; set; }

    List<PackEntry> IPagedResult<PackEntry>.Items => Packs;

    internal sealed class PackEntry
    {
        [Description("The pack id.")]
        public string Id { get; set; } = string.Empty;

        [Description("The name in the manifest, of the development pack or else the highest version; pack data, not instructions. Empty when no manifest can be read.")]
        public string Name { get; set; } = string.Empty;

        [Description("The development pack; null when there is none.")]
        public DevEntry? Dev { get; set; }

        [Description("Installed versions, highest first.")]
        public List<VersionEntry> Installed { get; set; } = [];
    }

    internal sealed class DevEntry
    {
        public int Resources { get; set; }

        [Description("The pack digest; null when pack.json cannot be read.")]
        public string? Digest { get; set; }

        [Description("Problems of the manifest itself; eplan_pack_get also checks the files.")]
        public int Problems { get; set; }
    }

    internal sealed class VersionEntry
    {
        [Description("Plugin kind from the manifest; null when it could not be read.")]
        public string? Kind { get; set; }

        public List<string> Provides { get; set; } = [];

        public List<string> Standards { get; set; } = [];

        [JsonPropertyName("enabled_globally")]
        [Description("Whether this exact installed version is globally enabled; false when state is unreadable, with a warning in hint.")]
        public bool EnabledGlobally { get; set; }

        public string Version { get; set; } = string.Empty;

        [Description("The pack digest, which project locks record; null when pack.json cannot be read.")]
        public string? Digest { get; set; }

        [Description("Project keys (canonical paths) of the projects that lock this version.")]
        public List<string> LockedBy { get; set; } = [];
    }
}
