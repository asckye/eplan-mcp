using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Core.Protocol;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests.Operations;

internal sealed class FakePropertySetSession : IPropertySetSession
{
    private readonly Dictionary<long, IPropertyReadScope> _lists = [];
    private bool _disposed;
    public Dictionary<long, FakePropertyReadAdapter> Objects { get; } = [];
    public List<SnapshotObject> Inventory { get; } = [];
    public Action<PropertySetGuard>? AfterWrite { get; set; }
    public Action? BeforeInventory { get; set; }
    public Action? OnDispose { get; set; }
    public int Writes { get; private set; }
    public int Disposals { get; private set; }

    public FakePropertySetSession()
    {
        for (int i = 1; i <= 2; i++)
        {
            Inventory.Add(PropertySetPlannerTests.Function(i, main: i == 1));
            FakePropertyReadAdapter obj = PropertySetPlannerTests.Adapter();
            obj.Properties.Add(20027, new FakeProperty
            {
                Metadata = new PropertyDefinitionMetadata
                {
                    ValueType = "opaque",
                    Format = "ValueWithUnit",
                    MaximumLength = 40,
                    IsIndexed = false,
                    IsInternal = false,
                    IsReadOnly = false,
                    IsNamePart = false,
                    IsPropertyLicensed = true,
                },
                Display = "14 W",
            });
            Objects.Add(i, obj);
        }
    }

    public IReadOnlyList<SnapshotObject> Functions
    {
        get
        {
            BeforeInventory?.Invoke();
            return Inventory;
        }
    }

    public IReadOnlyList<string> Languages => ["zh_CN", "de_DE"];

    public IPropertyReadScope Open(string projectPath, PropertyTarget target)
    {
        Assert.False(_disposed);
        long oid = target.Locator!.Oid;
        if (!_lists.TryGetValue(oid, out IPropertyReadScope? scope))
        {
            scope = Objects[oid].Open(projectPath, target);
            _lists.Add(oid, scope);
        }

        return new Borrowed(scope);
    }

    public void Write(PropertySetGuard guard)
    {
        Assert.False(_disposed);
        Assert.NotEmpty(_lists);
        Writes++;
        FakeProperty property = Objects[guard.Target.Oid].Properties[guard.PropertyId];
        property.Empty = guard.After.All(t => t.Text.Length == 0);
        if (guard.PropertyId == 20027)
        {
            property.Display = PropertyValueComparison.TextIn(guard.After, TextLanguages.Independent);
        }
        else
        {
            property.Texts.Clear();
            foreach (SnapshotText text in guard.After.Where(t => t.Text.Length > 0))
            {
                property.Texts.Add(text.Language, text.Text);
            }
        }

        AfterWrite?.Invoke(guard);
    }

    public PropertySetPlan Plan(List<PropertySetChange> changes) => new PropertySetPlanner(this).Plan(
        PropertySetPlannerTests.Project, 2026, changes, Functions, Languages, CancellationToken.None);

    public Action RestorePoint()
    {
        var original = Objects.ToDictionary(p => p.Key, p => BridgeJson.FromObject(p.Value.Properties));
        SnapshotObject[] functions = Inventory.Select(f => BridgeJson.FromObject(f).ToObject<SnapshotObject>(BridgeJson.Serializer)!).ToArray();
        return () =>
        {
            foreach (var pair in original)
            {
                Dictionary<int, FakeProperty> properties = pair.Value.ToObject<Dictionary<int, FakeProperty>>(BridgeJson.Serializer)!;
                Objects[pair.Key].Properties.Clear();
                foreach (var property in properties)
                {
                    Objects[pair.Key].Properties.Add(property.Key, property.Value);
                }
            }

            Inventory.Clear();
            Inventory.AddRange(functions);
        };
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Disposals++;
        foreach (IPropertyReadScope scope in _lists.Values)
        {
            scope.Dispose();
        }

        OnDispose?.Invoke();
    }

    public void AssertDisposed()
    {
        Assert.Equal(1, Disposals);
        foreach (FakePropertyReadAdapter obj in Objects.Values)
        {
            Assert.Equal(1, obj.Opens);
            obj.AssertDisposed();
        }
    }

    private sealed class Borrowed(IPropertyReadScope scope) : IPropertyReadScope
    {
        public string ObjectType => scope.ObjectType;
        public string DefaultLanguage => scope.DefaultLanguage;
        public IPropertyReadHandle? OpenProperty(int propertyId) => scope.OpenProperty(propertyId);
        public void Dispose() { }
    }
}
