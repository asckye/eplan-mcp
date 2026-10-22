using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Bridge.Eplan.Adapters.Generated;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Applies a value set to one placeholder the way spike S29 did (api-notes A33): through the generated overload table,
/// <see cref="PlaceHolderApi.ApplyRecord(PlaceHolder, string)"/>, that is <c>PlaceHolder.ApplyRecord(string)</c>; the
/// overload with <c>bApplyPageAndProjectData</c> is not verified and not called. S29 applied value sets inside a
/// SafetyPoint and rolled them back; the operation's transaction adds the UndoStep and QuietModeStep, and whether the
/// result stays after the commit and one Ctrl+Z undoes it is left to the Large test (tool spec, section 5). The
/// placeholder is found again as the operation chose it and read back with <see cref="EplanPlaceholderReader.Record"/>.
/// Main thread, inside the operation's transaction.
/// </summary>
internal sealed class EplanPlaceholderWriter : IPlaceholderWriter
{
    public PlaceholderRecord Apply(string projectPath, string page, string name, string valueSet)
    {
        MainThread.Assert();
        TextLanguage language = EplanText.Language(null);
        Project project = EplanProjects.ByPath(projectPath);
        using var finder = new DMObjectsFinder(project);
        PlaceHolder[] found = [.. (finder.GetPlaceholdersWithFilterScheme(null) ?? []).Where(h => h.Page?.Name == page && (h.Name ?? string.Empty) == name && HasValueSet(h, valueSet))];
        if (found.Length != 1)
        {
            throw new InvalidOperationException($"Expected one placeholder to apply the value set to, found {found.Length}.");
        }

        PlaceHolderApi.ApplyRecord(found[0], valueSet);
        return EplanPlaceholderReader.Record(found[0], language);
    }

    private static bool HasValueSet(PlaceHolder holder, string valueSet) =>
        Enumerable.Range(0, holder.NumberOfRecords).Any(i => holder.get_NameOfRecord(i) == valueSet);
}
