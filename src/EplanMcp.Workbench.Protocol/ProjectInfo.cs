namespace EplanMcp.Workbench.Protocol;

/// <summary>The project a question is about, as EPLAN has it open. Project data.</summary>
/// <param name="Name">The project's name.</param>
/// <param name="Path">The project's full path, without extension.</param>
public sealed record ProjectInfo(string Name, string Path);
