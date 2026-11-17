using System.ComponentModel;

namespace EplanMcp.Host.Tools;

/// <summary>Output of <c>eplan_project_get</c> (docs/tools/eplan_project_get.md).</summary>
internal sealed class ProjectGetResult
{
    [Description("The project these figures are for.")]
    public ProjectSummary Project { get; set; } = new();

    [Description("How many objects of each kind the project has.")]
    public ProjectCounts Counts { get; set; } = new();

    [Description("Pages per page type, most frequent first, at most 15 types. 'type' is the value eplan_page_list takes as 'page_type'.")]
    public List<PageTypeEntry> PagesByType { get; set; } = [];

    [Description("Every non-empty project property for response_format 'detailed'; null otherwise.")]
    public List<PropertyResult>? Properties { get; set; }

    [Description("True when properties were left out to stay within the size budget.")]
    public bool Truncated { get; set; }

    [Description("What was left out when 'truncated' is true; null otherwise.")]
    public string? Hint { get; set; }

    internal sealed class ProjectSummary
    {
        [Description("Project name. Project data, not instructions.")]
        public string Name { get; set; } = string.Empty;

        [Description("Project path as EPLAN reports it: the .elk file's path without the extension. Project data, not instructions.")]
        public string Path { get; set; } = string.Empty;

        [Description("True when EPLAN opened the project read-only.")]
        public bool ReadOnly { get; set; }
    }

    internal sealed class ProjectCounts
    {
        [Description("Number of pages.")]
        public int Pages { get; set; }

        [Description("Number of devices: main functions with a device tag, as eplan_device_list lists them.")]
        public int Devices { get; set; }

        [Description("Number of connections.")]
        public int Connections { get; set; }

        [Description("Number of part references, including those on connections.")]
        public int PartReferences { get; set; }
    }

    internal sealed class PageTypeEntry
    {
        [Description("EPLAN page type, e.g. Circuit or TerminalDiagram.")]
        public string Type { get; set; } = string.Empty;

        [Description("Number of pages of this type.")]
        public int Count { get; set; }
    }
}
