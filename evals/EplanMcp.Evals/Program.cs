namespace EplanMcp.Evals;

/// <summary>
/// <c>EplanMcp.Evals check</c> validates the task files; <c>run</c> runs them against EPLAN and a model
/// (agent evals design 4.4). Both come with the next steps of the design.
/// </summary>
internal static class Program
{
    private static int Main()
    {
        Console.Error.WriteLine("usage: EplanMcp.Evals check | run (not implemented yet: agent evals design, section 8)");
        return 2;
    }
}
