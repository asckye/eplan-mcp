using Eplan.EplApi.Base;

namespace Eplan.EplApi.MasterData;

internal sealed class MDPropertyValue(string text)
{
    public bool IsEmpty => text.Length == 0;
    public MultiLangString ToMultiLangString() => new(text);
    public override string ToString() => text;
}
