using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.HEServices;
using Eplan.EplApi.HEServices.Exceptions;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>A84: fresh wrappers for every read; no transient block survives CreateReport or commit.</summary>
internal sealed partial class EplanReportCreator : IReportCreator
{
    public IReadOnlyList<ReportPageRecord> ReportPages(string projectPath)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        var pages = new List<ReportPageRecord>();
        foreach (Page page in EplanNativePreconditions.Valid(finder.GetPages(null)))
        {
            bool unknown = false;
            PageReport? report = EplanReportReader.Read(page, () => unknown = true);
            if (report is not null || unknown)
            { pages.Add(new() { Name = page.Name, Id = page.ToStringIdentifier(), Type = report?.Type ?? string.Empty, Form = report?.Form ?? string.Empty }); }
        }
        return pages;
    }
    public void Create(string projectPath, ReportCreateArgs args)
    {
        MainThread.Assert();
        try
        {
            EplanReportForms.Ensure(EplanProjects.ByPath(projectPath), args.Form!);
            using var block = new ReportBlock();
            block.Create(EplanProjects.ByPath(projectPath));
            block.Type = (DocumentTypeManager.DocumentType)Enum.Parse(typeof(DocumentTypeManager.DocumentType), args.Type!);
            block.FormName = args.Form!;
            ApplySchemes(block, args);
            block.IsAutomaticPageDescription = args.PageDescription is null;
            if (args.PageDescription is not null)
            {
                using var text = new MultiLangString(); // A54: assign a language-independent value.
                text.AddString(ISOCode.Language.L___, args.PageDescription);
                block.ManualPageDescription = text;
            }
            using var reports = new Reports();
            reports.CreateReport(block, args.FirstPage!);
        }
        catch (Exception ex) when (ex is InvalidScheme or ArgumentException || ((ex is BaseException or ApplicationException) && new EplanExceptionMapper().Map(ex) is null))
        {
            throw new BridgeErrorException(ex is InvalidScheme or ArgumentException ? ErrorReason.InvalidArgument : ErrorReason.EplanOperationFailed, "EPLAN could not create the report; check the type, form, first page and schemes. See metadata.eplanMessage for EPLAN's message.", ex)
            { Error = { Metadata = new() { ["eplanMessage"] = EplanConnectionGenerator.ForModel(ex.Message) } } };
        }
    }
}
