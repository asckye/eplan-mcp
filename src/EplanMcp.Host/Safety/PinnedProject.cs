using System.Reflection;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Safety;

/// <summary>
/// Sends a write to the project its plan was made for. Policy, approval text and backup all use the plan's full project
/// path, but most tools build their execute arguments from the caller's <c>project</c>, often a bare name, which the
/// Bridge resolves again. Between approval and execution another open project could take that name (the scratch copy
/// closed, the real project of the same name opened), and the approved write would land there (security review
/// 2026-11-17). With the full path the Bridge finds exactly the planned project or none (ProjectResolver matches a
/// path first).
/// </summary>
internal static class PinnedProject
{
    /// <summary>
    /// <paramref name="args"/> with its <c>Project</c> set to the plan's project path. Arguments without a string
    /// <c>Project</c>, and plans without a project (master data only), are left as they are.
    /// </summary>
    public static TArgs In<TArgs>(TArgs args, WritePlan plan)
    {
        if (args is not null && plan.ProjectPath.Length > 0 && Property<TArgs>.Project is { } project)
        {
            project.SetValue(args, plan.ProjectPath);
        }
        return args;
    }

    private static class Property<TArgs>
    {
        public static readonly PropertyInfo? Project = typeof(TArgs).GetProperty("Project", BindingFlags.Public | BindingFlags.Instance) is { CanWrite: true } property
            && property.PropertyType == typeof(string) ? property : null;
    }
}
