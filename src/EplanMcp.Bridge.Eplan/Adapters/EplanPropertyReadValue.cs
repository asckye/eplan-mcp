using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>Owns one value and lends no native object to the detached result (A36).</summary>
internal sealed class EplanPropertyReadValue(PropertyValue value, Action check) : IPropertyReadValue
{
    public bool IsEmpty
    {
        get
        {
            check();
            return value.IsEmpty;
        }
    }

    public object ReadStoredValue()
    {
        check();
        using PropertyDefinition definition = value.Definition;
        check();
        PropertyDefinition.PropertyType type = definition.Type;
        check();
        return type switch
        {
            PropertyDefinition.PropertyType.Bool => value.ToBool(),
            PropertyDefinition.PropertyType.Long => (long)value.ToInt(),
            PropertyDefinition.PropertyType.Double or PropertyDefinition.PropertyType.Coord => value.ToDouble(),
            PropertyDefinition.PropertyType.String => value.ToString(),
            _ => throw new InvalidOperationException("This runtime type has no verified storage conversion."),
        };
    }

    public string? Display
    {
        get
        {
            check();
            return value.ToString();
        }
    }

    public IPropertyReadTexts OpenTexts()
    {
        check();
        return new Texts(value.ToMultiLangString(), check);
    }

    public void Dispose() => value.Dispose();

    private sealed class Texts(MultiLangString text, Action check) : IPropertyReadTexts
    {
        public IReadOnlyList<string> Languages
        {
            get
            {
                check();
                var languages = new LanguageList();
                int count = text.GetLanguageList(ref languages);
                var codes = new List<string>();
                for (int i = 0; i < count; i++)
                {
                    check();
                    using var code = new ISOCode(languages.get_Language(i));
                    check();
                    codes.Add(code.GetString());
                }

                return codes;
            }
        }

        public string GetText(string language)
        {
            check();
            if (language == TextLanguages.Independent)
            {
                return text.GetString(ISOCode.Language.L___);
            }

            using var iso = new ISOCode(language);
            ISOCode.Language number = iso.GetNumber();
            check();
            return text.GetString(number);
        }

        public void Dispose() => text.Dispose();
    }
}
