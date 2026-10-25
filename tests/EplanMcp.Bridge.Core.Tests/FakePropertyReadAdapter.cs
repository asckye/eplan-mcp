using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Core.Tests;

internal sealed class FakePropertyReadAdapter : IPropertyReadAdapter
{
    public Dictionary<int, FakeProperty> Properties { get; } = [];
    public List<Lease> Resources { get; } = [];
    public List<string> Events { get; } = [];
    public List<(int Offset, int Count)> IndexRequests { get; } = [];
    public List<(int Id, int? Index)> ValueRequests { get; } = [];
    public Action<string>? OnAccess { get; set; }
    public string DefaultLanguage { get; set; } = "zh_CN";
    public string? ObjectType { get; set; }
    public int Opens { get; private set; }
    public int PropertyOpens { get; private set; }
    public int StoredReads { get; private set; }
    public string? ProjectPath { get; private set; }
    public PropertyTarget? Target { get; private set; }

    public IPropertyReadScope Open(string projectPath, PropertyTarget target)
    {
        Opens++;
        ProjectPath = projectPath;
        Target = target;
        return Acquire(new Scope(this), "scope.opened");
    }

    private T Acquire<T>(T lease, string stage) where T : Lease
    {
        try
        {
            Touch(stage);
            return lease;
        }
        catch
        {
            lease.Dispose();
            throw;
        }
    }

    private void Touch(string stage)
    {
        Events.Add(stage);
        OnAccess?.Invoke(stage);
    }

    public void AssertDisposed()
    {
        Assert.NotEmpty(Resources);
        Assert.All(Resources, r => Assert.Equal(1, r.Disposals));
    }

    internal class Lease : IDisposable
    {
        protected FakePropertyReadAdapter Owner { get; }
        private readonly string _kind;
        private readonly List<Lease> _children = [];

        public Lease(FakePropertyReadAdapter owner, string kind)
        {
            Owner = owner;
            _kind = kind;
            owner.Resources.Add(this);
        }

        public int Disposals { get; private set; }

        protected void Own(string kind) => _children.Add(new Lease(Owner, kind));

        public void Dispose()
        {
            Disposals++;
            Assert.Equal(1, Disposals);
            foreach (Lease child in _children)
            {
                child.Dispose();
            }

            Owner.Events.Add("disposed." + _kind);
            GC.SuppressFinalize(this);
        }
    }

    private sealed class Scope(FakePropertyReadAdapter owner) : Lease(owner, "list"), IPropertyReadScope
    {
        public string ObjectType => Owner.ObjectType ?? (Owner.Target!.ProjectObject == true ? "Project" : "Page");

        public string DefaultLanguage
        {
            get
            {
                Owner.Touch("default_language");
                return Owner.DefaultLanguage;
            }
        }

        public IPropertyReadHandle? OpenProperty(int propertyId)
        {
            Owner.PropertyOpens++;
            Owner.Touch("property.lookup");
            return Owner.Properties.TryGetValue(propertyId, out FakeProperty? property)
                ? Owner.Acquire(new Handle(Owner, propertyId, property), "property.opened") : null;
        }
    }

    private sealed class Handle : Lease, IPropertyReadHandle
    {
        private readonly FakePropertyReadAdapter _owner;
        private readonly int _id;
        private readonly FakeProperty _property;

        public Handle(FakePropertyReadAdapter owner, int id, FakeProperty property) : base(owner, "id")
        {
            _owner = owner;
            _id = id;
            _property = property;
            Own("parent_value");
        }

        public IPropertyReadDefinition OpenDefinition() =>
            _owner.Acquire(new Definition(_owner, _property), "definition.opened");

        public IReadOnlyList<int> ReadIndexes(int offset, int count)
        {
            _owner.Touch("indexes");
            _owner.IndexRequests.Add((offset, count));
            Assert.InRange(count, 1, 200);
            return [.. _property.Indexes.Skip(offset).Take(count)];
        }

        public IPropertyReadValue? OpenValue(int? index)
        {
            _owner.ValueRequests.Add((_id, index));
            _owner.Touch("value.lookup");
            return _property.ValueApplicable ? _owner.Acquire(new Value(_owner, _property, index), "value.opened") : null;
        }
    }

    private sealed class Definition : Lease, IPropertyReadDefinition
    {
        private readonly FakePropertyReadAdapter _owner;
        private readonly FakeProperty _property;

        public Definition(FakePropertyReadAdapter owner, FakeProperty property) : base(owner, "definition")
        {
            _owner = owner;
            _property = property;
            Own("lower_bound");
            Own("upper_bound");
        }

        public PropertyDefinitionMetadata Metadata
        {
            get
            {
                _owner.Touch("metadata");
                return _property.Metadata;
            }
        }
    }

    private sealed class Value(FakePropertyReadAdapter owner, FakeProperty property, int? index) : Lease(owner, "value"), IPropertyReadValue
    {
        public bool IsEmpty
        {
            get
            {
                Owner.Touch("is_empty");
                Assert.False(property.Metadata.IsIndexed == true && index is null);
                return property.Empty;
            }
        }

        public object ReadStoredValue()
        {
            Owner.StoredReads++;
            Owner.Touch("stored");
            return property.Stored;
        }

        public string? Display
        {
            get
            {
                Owner.Touch("display");
                return property.Display;
            }
        }

        public IPropertyReadTexts OpenTexts() => Owner.Acquire(new Texts(Owner, property), "texts.opened");
    }

    private sealed class Texts(FakePropertyReadAdapter owner, FakeProperty property) : Lease(owner, "texts"), IPropertyReadTexts
    {
        public IReadOnlyList<string> Languages
        {
            get
            {
                Owner.Touch("languages");
                return [.. property.Texts.Keys];
            }
        }

        public string GetText(string language)
        {
            Owner.Touch("text");
            return property.Texts[language];
        }
    }
}

internal sealed class FakeProperty
{
    public PropertyDefinitionMetadata Metadata { get; set; } = new() { ValueType = "text", IsIndexed = false, IsInternal = false };
    public object Stored { get; set; } = "engineering text";
    public string? Display { get; set; } = "display text";
    public bool Empty { get; set; }
    public bool ValueApplicable { get; set; } = true;
    public IReadOnlyList<int> Indexes { get; set; } = [];
    public Dictionary<string, string> Texts { get; } = [];
}
