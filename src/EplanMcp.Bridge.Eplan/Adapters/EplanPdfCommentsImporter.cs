using System.Globalization;
using Eplan.EplApi.ApplicationFramework;
using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.Graphics;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>A39/S44: fixed import action and typed Comment read-back; never dispose EPLAN-owned objects.</summary>
internal sealed class EplanPdfCommentsImporter : IPdfCommentsImporter
{
    public IReadOnlyList<string> Identifiers(string projectPath)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        return finder.GetAll<Comment>(true).Cast<Comment>().Select(c => c.ToStringIdentifier()).ToArray();
    }

    public bool Import(string projectPath, string filePath)
    {
        MainThread.Assert();
        using var context = new ActionCallingContext();
        context.AddParameter("TYPE", "PDFCOMMENTS");
        context.AddParameter("PROJECTNAME", projectPath);
        context.AddParameter("IMPORTFILE", filePath);
#pragma warning disable RS0030 // A39/S44 verified this exact fixed import, inside TransactionScope only.
        var action = new ActionManager().FindAction("import");
#pragma warning restore RS0030
        return action is not null && action.Execute(context);
    }

    public List<PdfComment> Read(string projectPath, IReadOnlyList<string> identifiers)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        var wanted = new HashSet<string>(identifiers, StringComparer.Ordinal);
        var result = new List<PdfComment>();
        foreach (Comment comment in finder.GetAll<Comment>(true).Cast<Comment>().Where(c => wanted.Contains(c.ToStringIdentifier())))
        {
            OperationContext.Current.ThrowIfCancellationRequested();
            using CommentPropertyList properties = comment.Properties; // Acquire exactly once (A36/A51).
            using PropertyValue value = properties.TEXT_CONTENTS;
            using MultiLangString? text = value.IsEmpty ? null : value.ToMultiLangString();
            using AnyPropertyId positionId = 19103;
            using PropertyValue position = properties[positionId];
            result.Add(new PdfComment
            {
                Page = comment.Page.Name,
                Text = text?.GetString(ISOCode.Language.L___),
                Author = comment.Author,
                Created = comment.CreationDate.ToString("o", CultureInfo.InvariantCulture),
                Modified = comment.ModificationDate.ToString("o", CultureInfo.InvariantCulture),
                Position = position.IsEmpty ? null : position.ToString()
            });
        }

        return result.Count == wanted.Count ? result : throw new BridgeErrorException(ErrorReason.EplanOperationFailed,
            "New comments could not all be read back; the import was rolled back. Ask the engineer to check EPLAN.");
    }
}
