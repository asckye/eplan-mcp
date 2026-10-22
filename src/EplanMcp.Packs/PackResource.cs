using System.ComponentModel;

namespace EplanMcp.Packs;

[Description("A file, or an EPLAN project (link file and data folder), in the pack.")]
public sealed record PackResource
{
    [Description("The path in the pack, '/' between folders. A project's path is its link file (.elk, .ell, .elr); its data folder <name>.edb lies beside it and belongs to the same resource.")]
    public required string Path { get; init; }

    public required ResourceKind Kind { get; init; }

    [Description("A finer kind where EplanMcp knows one, such as window_macro, page_macro or plot_frame.")]
    public string? Subtype { get; init; }

    [Description("SHA-256, lowercase hex. A file: of its bytes. A project: of the lines '<path>\\t<bytes>\\t<sha256>\\n' of every file (link file and data folder), paths relative to the link file's folder with '/', sorted ordinally.")]
    public required string Sha256 { get; init; }

    [Description("The size in bytes; a project: all its files together.")]
    public required long Bytes { get; init; }

    [Description("The number of files: 1, or a project's files with the link file.")]
    public required int Files { get; init; }

    public required ResourceStorage Storage { get; init; }

    public ResourceSource? Source { get; init; }

    [Description("The EPLAN version the files say they were written with, when EplanMcp can read it without EPLAN; the evidence is in facts.")]
    public string? EplanVersion { get; init; }
}
