namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Owns the object's single list, not the borrowed StorableObject (A36/A39); never reacquire Properties.</summary>
public interface IPropertyReadScope : IDisposable
{
    /// <summary>The resolved object's dictionary type; catalog-declared base property lists participate in classification.</summary>
    public string ObjectType { get; }
    public string DefaultLanguage { get; }
    public IPropertyReadHandle? OpenProperty(int propertyId);
}
