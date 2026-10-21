using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Tests.Bridge;

/// <summary>
/// A fake EPLAN parts database shaped like the one spike S24 read (api-notes A27): the auxiliary contactor relay with a
/// macro, a second variant without one, parts of other manufacturers, and descriptions in English and Chinese. Like the
/// adapter, it checks the language first: "xx_XX" is one EPLAN does not know; <see cref="Broken"/> stands for a database
/// that cannot be opened.
/// </summary>
internal sealed class SamplePartsDatabase : IPartsDatabase
{
    public bool Broken { get; set; }

    public PartsDatabaseContent Read(string? language)
    {
        if (language == "xx_XX")
        {
            throw new BridgeErrorException(ErrorReason.InvalidArgument, "'language' is not a language EPLAN knows.");
        }

        if (Broken)
        {
            throw new BridgeErrorException(ErrorReason.EplanOperationFailed, "EPLAN could not open or read its parts database.");
        }

        bool chinese = language == "zh_CN";
        return new PartsDatabaseContent
        {
            Database = @"$(MD_PARTS)\EPLAN_parts.alk",
            Parts =
            [
                Part("SIE.3RT2016-1BB41", "1", "SIE", "3RT2016-1BB41", chinese ? "接触器" : "Contactor, AC-3, 4 kW/400 V", macro: true),
                Part("SIE.3RH2122-1HB40", "2", "SIE", "3RH2122-1HB40", chinese ? "辅助接触器" : "COUPLING CONTACTOR RELAY, variant", macro: false),
                Part("SIE.3RH2122-1HB40", "1", "SIE", "3RH2122-1HB40", chinese ? "辅助接触器" : "COUPLING CONTACTOR RELAY, 2NO+2NC", macro: true),
                Part("PXC.3031212", "1", "PXC", "ST 2,5", chinese ? "端子" : "Feed-through terminal block", macro: true),
            ],
        };
    }

    private static PartSearchItem Part(string number, string variant, string manufacturer, string type, string description, bool macro) => new()
    {
        PartNumber = number,
        Variant = variant,
        Manufacturer = manufacturer,
        TypeNumber = type,
        OrderNumber = type.Replace("-", string.Empty, StringComparison.Ordinal),
        ProductGroup = "ElectricalRelay",
        Description = description,
        HasMacro = macro,
    };
}
