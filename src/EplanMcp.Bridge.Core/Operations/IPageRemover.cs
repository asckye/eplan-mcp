namespace EplanMcp.Bridge.Core.Operations;

/// <summary>
/// Reads and removes pages for eplan_page_delete (docs/tools/eplan_page_delete.md, section 5), implemented by the EPLAN
/// adapter layer. Main thread only; <see cref="Remove"/> only inside the write's transaction.
/// </summary>
public interface IPageRemover
{
    /// <summary>
    /// The page whose full name is exactly <paramref name="pageName"/> (ordinal): its type and the session identifiers of
    /// the page and of every placement on it; null when there is none.
    /// </summary>
    public RemovablePage? ReadPage(string projectPath, string pageName);

    /// <summary>Every function of the project, main and auxiliary, in EPLAN's order, with its page and session identifier.</summary>
    public IReadOnlyList<PlacedFunction> Functions(string projectPath);

    /// <summary>Removes the page whose full name is exactly <paramref name="pageName"/> (<c>Page.Remove()</c>).</summary>
    public void Remove(string projectPath, string pageName);
}

/// <summary>A page as <see cref="IPageRemover.ReadPage"/> reads it.</summary>
public sealed class RemovablePage
{
    public string Name { get; set; } = string.Empty;

    /// <summary>EPLAN's page type, e.g. <c>Circuit</c>.</summary>
    public string Type { get; set; } = string.Empty;

    /// <summary>The session identifiers of the page and of every placement on it.</summary>
    public List<string> ObjectIds { get; set; } = [];
}

/// <summary>A function as <see cref="IPageRemover.Functions"/> reads it.</summary>
public sealed class PlacedFunction
{
    /// <summary><c>Function.Name</c>, the full tag; empty for a function without a name.</summary>
    public string Name { get; set; } = string.Empty;

    public bool IsMain { get; set; }

    /// <summary>The full name of the page it is placed on; null when it is not placed.</summary>
    public string? Page { get; set; }

    /// <summary>The session identifier (<c>ToStringIdentifier()</c>).</summary>
    public string Id { get; set; } = string.Empty;
}
