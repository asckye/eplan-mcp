using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters.Generated;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads a project's placeholder objects the way spike S29 did (api-notes A33): every one through
/// <c>DMObjectsFinder.GetPlaceholdersWithFilterScheme(null)</c>, the navigator without a filter; the name, the value set
/// and variable counts and the variable names through the generated <see cref="PlaceHolderApi.Read"/>; the value sets'
/// names through <c>get_NameOfRecord(i)</c> (an indexed property, called as its accessor); the referenced objects; each
/// value through <c>GetValue</c> in the language with fallback, the raw <c>get_Value</c> naming the languages it has
/// (EplanText); and the current values through <c>GetAllCurrentValues()</c>. Main thread, inside the dispatcher's
/// LockingStep; nothing is opened, applied or changed.
/// </summary>
internal sealed class EplanPlaceholderReader : IPlaceholderReader
{
    public IReadOnlyList<PlaceholderRecord> Read(string projectPath, string? language)
    {
        MainThread.Assert();
        TextLanguage textLanguage = EplanText.Language(language);
        Project project = EplanProjects.ByPath(projectPath);
        using var finder = new DMObjectsFinder(project);
        return [.. (finder.GetPlaceholdersWithFilterScheme(null) ?? []).Select(holder => Record(holder, textLanguage))];
    }

    /// <summary>One placeholder copied into a record; the write adapter reads the one it changes the same way.</summary>
    internal static PlaceholderRecord Record(PlaceHolder holder, TextLanguage language)
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

        foreach (PropertyValue? value in holder.GetAllCurrentValues() ?? [])
        {
            (string? text, string? fallback) = value is null ? (null, null) : EplanText.ReadWithLanguage(value, language);
            record.CurrentValues.Add(new PlaceholderText { Text = text, Language = fallback, Raw = value is null || value.IsEmpty ? null : value.ToString() });
        }

        return record;
    }
}
