// A small public API with the shapes the EPLAN assemblies use, for the scanner tests: the C# compiler writes the
// documentation ID of every commented member to EplanMcp.Catalog.Tests.xml, and the scanner must spell each one the
// same way. Every member therefore has a doc comment, and every class declares its constructors.
// It imitates the shapes, not the design: members without state (CA1822) and an enum value named after its enum
// (CA1712), as in EPLAN's Properties enums.
#pragma warning disable CA1822, CA1712

namespace EplanMcp.Catalog.Tests.Fixture;

/// <summary>Methods, properties, fields and nested types of every visibility.</summary>
public class Widget : IShape
{
    /// <summary>A constant.</summary>
    public const int Limit = 3;

    /// <summary>A static field.</summary>
    public static readonly string Default = "w";

    /// <summary>Parameterless constructor.</summary>
    public Widget()
    {
    }

    /// <summary>Constructor with parameters.</summary>
    public Widget(string name, int[] sizes)
    {
        Name = name + sizes.Length;
    }

    /// <summary>An event with explicit accessors.</summary>
    public event EventHandler? Changed
    {
        add { }
        remove { }
    }

    /// <summary>A read-write property.</summary>
    public string Name { get; set; } = "";

    /// <summary>A read-only property.</summary>
    public int Count { get; }

    /// <summary>An indexer.</summary>
    public string this[int index] => Name + index;

    /// <summary>A conversion operator.</summary>
    public static implicit operator string(Widget widget) => widget.Name;

    /// <summary>A static method with a multi-dimensional array.</summary>
    public static int Sum(int[,] grid) => grid.Length;

    /// <summary>By-reference parameters.</summary>
    public void Move(ref double x, out double y) => y = x + Count;

    /// <summary>A generic method with generic parameters.</summary>
    public T Pick<T>(IList<T> items, Dictionary<string, T> map) => map.Count > 0 ? map.Values.First() : items[Count];

    /// <summary>An override of an object member.</summary>
    public override string ToString() => Name;

    /// <summary>An explicit interface implementation: private, so not API.</summary>
    void IShape.Draw()
    {
    }

    /// <summary>Visible to derived types only.</summary>
    protected void OnChanged() => Name += "!";

    /// <summary>Internal: not API.</summary>
    internal void Hidden() => Name += "?";

    /// <summary>A public nested type.</summary>
    public sealed class Part
    {
        /// <summary>Constructor.</summary>
        public Part()
        {
        }

        /// <summary>A member of a nested type.</summary>
        public void Fit()
        {
        }
    }

    /// <summary>A private nested type: not API.</summary>
    private sealed class Secret
    {
        /// <summary>Constructor.</summary>
        public Secret()
        {
        }

        /// <summary>A public member of a private type: not API.</summary>
        public void Hide()
        {
        }
    }
}

/// <summary>A generic type.</summary>
public sealed class Box<T>
{
    /// <summary>Constructor.</summary>
    public Box()
    {
    }

    /// <summary>Uses the type parameter.</summary>
    public void Put(T item, Box<T> other)
    {
    }
}

/// <summary>An enum.</summary>
public enum Shade
{
    /// <summary>First value.</summary>
    Light = 1,

    /// <summary>Second value.</summary>
    Dark = 2,
}

/// <summary>An interface.</summary>
public interface IShape
{
    /// <summary>An interface member.</summary>
    public void Draw();
}

/// <summary>A delegate.</summary>
public delegate void ShapeHandler(IShape shape);

/// <summary>A struct.</summary>
public struct Point
{
    /// <summary>A struct property.</summary>
    public double X { get; set; }
}

/// <summary>Shaped like an EPLAN property list: upper-case properties are property IDs of the Widget object.</summary>
public sealed class WidgetPropertyList
{
    /// <summary>Constructor.</summary>
    public WidgetPropertyList()
    {
    }

    /// <summary>A property ID.</summary>
    public int WIDGET_NAME => 1;

    /// <summary>Not a property ID: the generic accessor.</summary>
    public int Property => 2;
}

/// <summary>Shaped like EPLAN's Properties class: nested enums carry the numeric property IDs.</summary>
public static class Properties
{
    /// <summary>The Widget object's property IDs.</summary>
    public enum Widget
    {
        /// <summary>A property ID.</summary>
        WIDGET_NAME = 20001,
    }
}

/// <summary>An obsolete type.</summary>
[Obsolete("Fixture only.")]
public static class Legacy
{
    /// <summary>A member of an obsolete type.</summary>
    public static void Run()
    {
    }
}

/// <summary>An internal type: not API.</summary>
internal sealed class InternalType
{
    /// <summary>A public member of an internal type: not API.</summary>
    public static void Run()
    {
    }
}
