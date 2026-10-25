namespace EplanMcp.Bridge.Core.Operations;

/// <summary>Owns the object's single list, not the borrowed StorableObject (A36/A39); never reacquire Properties.</summary>
public interface IPropertyReadScope : IDisposable
{
    public string DefaultLanguage { get; }
    public IPropertyReadHandle? OpenProperty(int propertyId);
}
