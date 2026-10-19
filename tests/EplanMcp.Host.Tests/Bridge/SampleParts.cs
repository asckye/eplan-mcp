using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Host.Tests.Bridge;

/// <summary>
/// A fake EPLAN part adapter shaped like spike S6's sample project (api-notes A11): several references to one part on
/// one device, references on wires, a variant, a part the project has no part data for, a reference without a part
/// number, and descriptions in two languages. Like the adapter, it checks the language first: "xx_XX" is one EPLAN
/// does not know; null stands for EPLAN's user interface language (English here).
/// </summary>
internal sealed class SampleParts : IPartReader
{
    public List<PartReferenceRecord> References { get; } =
    [
        new() { PartNumber = "SIE.3RT2016-1BB41", Count = 1, Owner = PartOwner.Function, OwnerName = "=GAA+A1-QA1" },
        new() { PartNumber = "SIE.3RT2016-1BB41", Count = 1, Owner = PartOwner.Function, OwnerName = "=GAA+A1-QA1" },
        new() { PartNumber = "SIE.3RT2016-1BB41", Count = 1, Owner = PartOwner.Function, OwnerName = "=GAA+A1-QA2" },
        new() { PartNumber = "SIE.3RV2011-1JA10", Count = 1, Owner = PartOwner.Function, OwnerName = "=GAA+A1-FC1" },
        new() { PartNumber = "RIT.8601000", Variant = "2", Count = 1, Owner = PartOwner.Function, OwnerName = "=GAA+A1-UA1" },
        new() { PartNumber = "LAPP.4510011", Count = 1, Owner = PartOwner.Connection },
        new() { PartNumber = "LAPP.4510011", Count = 2, Owner = PartOwner.Connection },
        new() { PartNumber = string.Empty, Count = 1, Owner = PartOwner.Function, OwnerName = "=GAA+A1-XD1" },
    ];

    public List<(string PartNumber, string Variant, string English, string Chinese, string Manufacturer)> Articles { get; } =
    [
        ("SIE.3RT2016-1BB41", string.Empty, "Contactor, AC-3, 4 kW/400 V", "接触器，AC-3，4 kW/400 V", "SIE"),
        ("SIE.3RV2011-1JA10", string.Empty, "Circuit breaker, 0.7-1 A", "断路器，0.7-1 A", "SIE"),
        ("RIT.8601000", "2", "Enclosure, 600x2000x500 mm", "柜体，600x2000x500 mm", "RIT"),
    ];

    public ProjectParts ReadParts(string projectPath, string? language)
    {
        if (language is not null and not "en_US" and not "zh_CN")
        {
            throw new BridgeErrorException(ErrorReason.InvalidArgument, $"'language' {language} is not a language EPLAN knows.");
        }

        bool chinese = language == "zh_CN";
        return new ProjectParts
        {
            References = References,
            Articles =
            [
                .. Articles.Select(a => new ArticleRecord
                {
                    PartNumber = a.PartNumber,
                    Variant = a.Variant,
                    Description = chinese ? a.Chinese : a.English,
                    Manufacturer = a.Manufacturer,
                }),
            ],
        };
    }
}
