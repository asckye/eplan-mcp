using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters.Generated;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads a project's placeholder objects the way spike S29 did (api-notes A33): every one through
/// <c>DMObjectsFinder.GetPlaceholdersWithFilterScheme(null)</c>, the navigator without a filter; the name, the value set
/// and variable counts and the variable names through the generated <see cref="PlaceHolderApi.Read"/>; the value sets'
/// names through <c>get_NameOfRecord(i)</c> (an indexed property, called as its accessor); the referenced objects; and
/// each value through <c>GetValue</c> in the language with fallback, the raw <c>get_Value</c> naming the languages it
/// has (EplanText). The current values (<c>GetAllCurrentValues()</c>) only when asked: EPLAN counts reading them as
/// write access and throws <c>LockingException</c> for a placeholder that is not locked (2026-10-22, the Large tests'
/// write copy), and a read takes no locks. Main thread, inside the dispatcher's LockingStep; nothing is opened, applied
/// or changed.
/// </summary>
internal sealed class EplanPlaceholderReader : IPlaceholderReader
{
    public IReadOnlyList<PlaceholderRecord> Read(string projectPath, string? language, bool currentValues)
    {
        MainThread.Assert();
        TextLanguage textLanguage = EplanText.Language(language);
        Project project = EplanProjects.ByPath(projectPath);
        using var finder = new DMObjectsFinder(project);
        return [.. (finder.GetPlaceholdersWithFilterScheme(null) ?? []).Select(holder => Record(holder, textLanguage, currentValues))];
    }

    /// <summary>
    /// One placeholder copied into a record; the write adapter reads the one it changes the same way, inside its
    /// transaction. Current values EPLAN refuses for want of a lock are marked refused, not thrown.
    /// </summary>
    internal static PlaceholderRecord Record(PlaceHolder holder, TextLanguage language, bool currentValues)
    {
        var record = new PlaceholderRecord
        {
            Properties = PlaceHolderApi.Read(holder),
            Page = holder.Page?.Name,
            AssignedObjects = holder.AssignedObjects?.Length ?? 0,
        };
        for (int i = 0; i < record.Properties.NumberOfRecords; i++)
        {
            record.ValueSets.Add(holder.get_NameOfRecord(i) ?? string.Empty);
        }

        foreach (string valueSet in record.ValueSets)
        {
            foreach (string variable in record.Properties.VariableNames)
            {
                string raw = holder.get_Value(valueSet, variable) ?? string.Empty;
                (string? text, string? fallback) = EplanText.ReadWithLanguage(() => holder.GetValue(valueSet, variable), raw, language);
                record.Values.Add(new PlaceholderValueRecord
                {
                    ValueSet = valueSet,
                    Variable = variable,
                    Value = new PlaceholderText { Text = text, Language = fallback, Raw = raw },
                });
            }
        }

        if (currentValues)
        {
            try
            {
                record.CurrentValues = CurrentValues(holder, language);
            }
            catch (LockingException)
            {
                // The placeholder is not locked, as in a read (api-notes A33); the tool says so instead of failing.
                record.CurrentValuesRefused = true;
            }
        }

        return record;
    }

    /// <summary>
    /// <c>GetAllCurrentValues()</c>, one text per referenced property. Throws <c>LockingException</c> when the placeholder
    /// is not locked; inside a write's SafetyPoint it is (A14).
    /// </summary>
    internal static List<PlaceholderText> CurrentValues(PlaceHolder holder, TextLanguage language) =>
    [
        .. (holder.GetAllCurrentValues() ?? []).Select(value =>
        {
            (string? text, string? fallback) = value is null ? (null, null) : EplanText.ReadWithLanguage(value, language);
            return new PlaceholderText { Text = text, Language = fallback, Raw = value is null || value.IsEmpty ? null : value.ToString() };
        }),
    ];
}
