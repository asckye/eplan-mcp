using System.Globalization;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Tests.EndToEnd;

/// <summary>Real property.read traversal and sanitization over detached fake native resources.</summary>
internal sealed class PropertyTestAdapter : IPropertyReadAdapter
{
    public IPropertyReadScope Open(string projectPath, PropertyTarget target)
    {
        if (target.DeviceTag == "ambiguous")
        {
            throw new BridgeErrorException(ErrorReason.AmbiguousName, "Select a candidate locator.", metadata: new()
            {
                ["candidates"] = "[{\"project\":\"Line1\",\"kind\":\"function\",\"type\":17,\"oid\":42}]",
            });
        }
        return new Scope(target);
    }

    private sealed class Scope(PropertyTarget target) : IPropertyReadScope
    {
        public string ObjectType => target.ProjectObject == true ? "Project" : target.DeviceTag is not null ? "Function"
            : target.Locator?.Kind == "connection" ? "Connection" : "Page";

        public string DefaultLanguage => "en_US";

        public IPropertyReadHandle OpenProperty(int propertyId) => new Handle(propertyId);

        public void Dispose()
        {
        }
    }

    private sealed class Handle(int id) : IPropertyReadHandle
    {
        public IPropertyReadDefinition OpenDefinition() => new Definition(id);

        public IReadOnlyList<int> ReadIndexes(int offset, int count) => [.. Enumerable.Range(0, 205).Select(i => 10 + 2 * i).Skip(offset).Take(count)];

        public IPropertyReadValue OpenValue(int? index) => new Value(id, index);

        public void Dispose()
        {
        }
    }

    private sealed class Definition(int id) : IPropertyReadDefinition
    {
        public PropertyDefinitionMetadata Metadata => new()
        {
            ValueType = id == 20011 ? "multi_language" : "text",
            IsIndexed = id == 10901,
            IsInternal = false,
            IsReadOnly = false,
            IsComputed = false,
        };

        public void Dispose()
        {
        }
    }

    private sealed class Value(int id, int? index) : IPropertyReadValue
    {
        public bool IsEmpty => false;

        public string Display => id == 10014 ? "C:/private/customer.txt" : "row " + index?.ToString(CultureInfo.InvariantCulture);

        public object ReadStoredValue() => Display;

        public IPropertyReadTexts OpenTexts() => new Texts();

        public void Dispose()
        {
        }
    }

    private sealed class Texts : IPropertyReadTexts
    {
        public IReadOnlyList<string> Languages => ["en_US", "zh_CN"];

        public string GetText(string language) => language == "zh_CN" ? "explicit text" : "default text";

        public void Dispose()
        {
        }
    }
}
