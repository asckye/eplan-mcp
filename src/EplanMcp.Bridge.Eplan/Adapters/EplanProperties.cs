using System.Globalization;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads every non-empty property of an object for the detailed <c>*_get</c> results (M2 design 4.1). Main thread,
/// inside the dispatcher's LockingStep. Every DataModel property list (project, page, function) derives from
/// <see cref="UniversalPropertyList"/>.
/// </summary>
internal static class EplanProperties
{
    /// <summary>
    /// <c>ExistingIds</c> and <c>ExistingValues</c> list every property that exists on the object, empty ones included
    /// (EPLAN XML docs; S6: 1000 in 4 ms); S6 found values that are not <c>IsEmpty</c> but display as "", which
    /// <see cref="EplanText.Read"/> drops. An indexed property throws <c>IndexedPropertyException</c> when read without
    /// an index, even for <c>IsEmpty</c> (S6), so its used indexes are read one by one. Properties EPLAN marks internal
    /// are left out, as the XML docs ask.
    /// </summary>
    public static List<PropertyEntry> Read(UniversalPropertyList properties, ISOCode.Language language)
    {
        AnyPropertyId[] ids = properties.ExistingIds;
        PropertyValue[] values = properties.ExistingValues;
        var entries = new List<PropertyEntry>();
        for (int i = 0; i < Math.Min(ids.Length, values.Length); i++)
        {
            try
            {
                Add(entries, ids[i], values[i], language);
            }
            catch (Exception ex) when (ex is IndexedPropertyException or NotIndexedPropertyException or EmptyPropertyException or PropertyNotFoundException)
            {
                // One property EPLAN cannot read as listed costs that property, not the whole result; which ones do is
                // not verified on real projects yet (M2 Large tests).
            }
        }

        return entries;
    }

    private static void Add(List<PropertyEntry> entries, AnyPropertyId id, PropertyValue value, ISOCode.Language language)
    {
        PropertyDefinition definition = value.Definition;
        if (definition.IsInternal)
        {
            return;
        }

        // AsString is the number of a built-in property and the identifying name of a user-defined one (EPLAN docs).
        string key = id.AsString;
        int? number = int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;
        string name = string.IsNullOrEmpty(definition.Name) ? key : definition.Name;
        if (!definition.IsIndexed)
        {
            if (EplanText.Read(value, language) is { } text)
            {
                entries.Add(new PropertyEntry { Id = number, Name = name, Value = text });
            }

            return;
        }

        foreach (int index in value.Indexes)
        {
            if (EplanText.Read(value[index], language) is { } text)
            {
                entries.Add(new PropertyEntry { Id = number, Index = index, Name = name, Value = text });
            }
        }
    }
}
