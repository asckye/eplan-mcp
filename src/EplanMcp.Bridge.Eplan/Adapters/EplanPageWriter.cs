using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Creates pages the way spike S9 did (api-notes A13): the name parts as <see cref="PagePropertyList"/> properties,
/// then <c>Page.Create(project, type, parts)</c>, then the description as a <see cref="MultiLangString"/> holding one
/// language, which replaces the whole value. Main thread, inside the write's TransactionScope.
/// </summary>
internal sealed class EplanPageWriter : IPageWriter
{
    public bool KnowsLanguage(string language)
    {
        using var iso = new ISOCode(language);
        return iso.IsValid();
    }

    public PageSummary Create(string projectPath, PageName name, string pageType, string? description, string? language)
    {
        MainThread.Assert();
        var textLanguage = new TextLanguage(EplanText.Language(language).Number, fallback: false);
        Project project = EplanProjects.ByPath(projectPath);
        using var parts = new PagePropertyList();
        if (name.Plant is not null)
        {
            parts.DESIGNATION_PLANT = name.Plant;
        }

        if (name.Location is not null)
        {
            parts.DESIGNATION_LOCATION = name.Location;
        }

        if (name.DocumentType is not null)
        {
            parts.DESIGNATION_DOCTYPE = name.DocumentType;
        }

        parts.PAGE_COUNTER = name.Counter;

#pragma warning disable CA2000 // Spikes S0–S10 never disposed a new Page before the commit; doing so inside a SafetyPoint is untested.
        var page = new Page();
#pragma warning restore CA2000
        try
        {
            page.Create(project, (DocumentTypeManager.DocumentType)Enum.Parse(typeof(DocumentTypeManager.DocumentType), pageType), parts);
        }
        catch (NameConflictException ex)
        {
            var exists = new BridgeErrorException(
                ErrorReason.AlreadyExists,
                "EPLAN reports that a page with this name exists already, so nothing was created. Choose an unused page number; eplan_page_list with name_prefix shows the numbers in use.",
                ex);
            exists.Error.Metadata = new Dictionary<string, string> { ["existing"] = name.Text };
            throw exists;
        }

        if (description is not null)
        {
            using var text = new MultiLangString();
            text.AddString(textLanguage.Number, description);
            page.Properties.PAGE_NOMINATIOMN = text;
        }

        return new PageSummary
        {
            Name = page.Name,
            Type = page.PageType.ToString(),
            Description = EplanText.Read(page.Properties.PAGE_NOMINATIOMN, textLanguage),
        };
    }
}
