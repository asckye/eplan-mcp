using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests;

/// <summary>
/// Cables numbered the way S21 found <c>CableService.RenumberCables</c> numbers them (api-notes A24): the sample project's
/// only scheme, and the tags in <see cref="Numbers"/> change on the cables given (all without any); with
/// <c>keepExisting</c> only tags with '?' change (XML). <see cref="Refuse"/> stands for EPLAN's InvalidScheme with S12's
/// message, <see cref="NewIds"/> for EPLAN giving the functions new session identifiers. Records every numbering.
/// </summary>
internal sealed class FakeCableRenumberer : ICableRenumberer
{
    public List<string> SchemeNames { get; } = ["Standard", SchemeChoice.Internal];

    public List<PlacedFunction> Cables { get; } = [];

    public Dictionary<string, string> Numbers { get; } = new(StringComparer.Ordinal);

    public bool NewIds { get; set; }

    public bool Refuse { get; set; }

    public List<(IReadOnlyList<string>? Cables, string Scheme, bool KeepExisting, int Start, int Step)> Renumbers { get; } = [];

    public IReadOnlyList<string> Schemes(string projectPath) => SchemeNames;

    public IReadOnlyList<PlacedFunction> ReadCables(string projectPath) => [.. Cables.Select(c => new PlacedFunction { Name = c.Name, IsMain = c.IsMain, Page = c.Page, Id = c.Id })];

    public void Renumber(string projectPath, IReadOnlyList<string>? cables, string scheme, bool keepExisting, int start, int increment)
    {
        Renumbers.Add((cables, scheme, keepExisting, start, increment));
        if (Refuse)
        {
            throw SchemeChoice.Refused(scheme, FakeDeviceRenumberer.InvalidSchemeMessage, "cable", new InvalidOperationException(FakeDeviceRenumberer.InvalidSchemeMessage));
        }

        foreach (PlacedFunction cable in Cables.Where(c => cables is null || cables.Contains(c.Name)).ToList())
        {
            if ((!keepExisting || DeviceRenumberRequest.Unnumbered(cable.Name)) && Numbers.TryGetValue(cable.Name, out string? tag))
            {
                cable.Name = tag;
            }

            if (NewIds)
            {
                cable.Id += "'";
            }
        }
    }
}
