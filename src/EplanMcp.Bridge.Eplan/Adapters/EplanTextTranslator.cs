using Eplan.EplApi.Base;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>A text looked up in the translation database, by the project's source language (api-notes A28), with <see cref="Translate.TranslateText"/>.</summary>
internal sealed class EplanTextTranslator : ITextTranslator
{
    public string Translate(string projectPath, string text)
    {
        MainThread.Assert();
        try
        {
            using var translate = new Translate();
            using MultiLangString translated = translate.TranslateText(text, EplanProjects.ByPath(projectPath));
            return translated.GetAsString();
        }
        catch (BaseException ex)
        {
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN could not look a text up in the translation database: " + ex.Message, ex);
        }
    }
}
