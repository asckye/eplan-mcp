using System.Reflection;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Coverage.Scanning;

/// <summary>
/// One Bridge operation: its name, its class, and for a class that serves several operations the value of the enum its
/// constructor takes (ExportPlanOperation with ExportKind.Pdf is export.pdf.plan).
/// </summary>
internal sealed record OperationRoot(string Name, string ClassName, string? KindType = null, string? KindValue = null)
{
    /// <summary>
    /// Every operation class of Bridge.Core, instantiated with null adapters and each value of its kind enum, so that
    /// the name comes from the class itself rather than from a guess. A kind the class does not serve throws and is
    /// skipped.
    /// </summary>
    public static List<OperationRoot> FromBridgeCore()
    {
        var roots = new List<OperationRoot>();
        foreach (Type type in typeof(IOperation).Assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IOperation).IsAssignableFrom(t)))
        {
            ConstructorInfo constructor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).First();
            ParameterInfo[] parameters = constructor.GetParameters();
            ParameterInfo? kind = parameters.FirstOrDefault(p => p.ParameterType.IsEnum);
            object?[] kinds = kind is null ? [null] : [.. Enum.GetValues(kind.ParameterType).Cast<object>()];
            foreach (object? value in kinds)
            {
                object?[] args = [.. parameters.Select(p => p == kind ? value : p.ParameterType.IsValueType ? Activator.CreateInstance(p.ParameterType) : null)];
                try
                {
                    string name = ((IOperation)constructor.Invoke(args)).Name;
                    roots.Add(new OperationRoot(name, type.FullName!, kind?.ParameterType.FullName, value?.ToString()));
                }
                catch (Exception ex) when (ex is TargetInvocationException or ArgumentException)
                {
                    // Not an operation of this class (its name switch throws for the kind), or a constructor that checks
                    // its arguments; neither registers an operation in BridgeHost.
                }
            }
        }

        return [.. roots.OrderBy(r => r.Name, StringComparer.Ordinal)];
    }
}
