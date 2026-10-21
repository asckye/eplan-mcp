using EplanMcp.Bridge.Core.Operations;

namespace EplanMcp.Bridge.Core.Tests;

/// <summary>
/// Connections numbered the way S21 found <c>ConnectionService.DesignateConnections</c> numbers them (api-notes A24): the
/// wire numbers in <see cref="Numbers"/>, by connection identifier, replace the existing ones as the overwrite mode
/// allows, those in <see cref="Manual"/> counting as entered by hand. <see cref="Refuse"/> stands for EPLAN's
/// InvalidScheme with S12's message, <see cref="NewIds"/> for EPLAN giving the connections new identifiers. Records every
/// numbering.
/// </summary>
internal sealed class FakeConnectionNumberer : IConnectionNumberer
{
    public List<string> SchemeNames { get; } = ["Standard", "Potenzialorientiert", "Verbindungsorientiert", SchemeChoice.Internal];

    public List<DesignatedConnection> Connections { get; } = [];

    public Dictionary<string, string> Numbers { get; } = new(StringComparer.Ordinal);

    public HashSet<string> Manual { get; } = new(StringComparer.Ordinal);

    public bool Refuse { get; set; }

    public bool NewIds { get; set; }

    public List<(string Scheme, string Overwrite)> Designations { get; } = [];

    public IReadOnlyList<string> Schemes(string projectPath) => SchemeNames;

    public IReadOnlyList<DesignatedConnection> ReadConnections(string projectPath) =>
        [.. Connections.Select(c => new DesignatedConnection { Id = c.Id, Designation = c.Designation, Page = c.Page })];

    public void Designate(string projectPath, string scheme, string overwrite)
    {
        Designations.Add((scheme, overwrite));
        if (Refuse)
        {
            throw SchemeChoice.Refused(scheme, FakeDeviceRenumberer.InvalidSchemeMessage, "wire", new InvalidOperationException(FakeDeviceRenumberer.InvalidSchemeMessage));
        }

        foreach (DesignatedConnection connection in Connections)
        {
            bool replace = connection.Designation is null || overwrite == "all" || (overwrite == "except_manual" && !Manual.Contains(connection.Id));
            if (replace && Numbers.TryGetValue(connection.Id, out string? number))
            {
                connection.Designation = number;
            }

            if (NewIds)
            {
                connection.Id += "'";
            }
        }
    }
}
