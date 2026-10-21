using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.EObjects;
using Eplan.EplApi.HEServices.Exceptions;
using EplanMcp.Bridge.Core;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;
using EplanRenumber = Eplan.EplApi.HEServices.Renumber;
using TerminalEnums = Eplan.EplApi.HEServices.Renumber.Enums;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Numbers terminals the way spike S21 did (api-notes A24): the terminals of the strips in scope, or every terminal from
/// <c>GetTerminals(null)</c>, given to <c>Renumber.Terminals</c> with S21's options; the schemes from the user setting
/// S21 found. Main thread, inside the dispatcher's LockingStep; <see cref="Renumber"/> inside the write's TransactionScope,
/// whose SafetyPoint S21 saw roll the numbering back.
/// </summary>
internal sealed class EplanTerminalRenumberer : ITerminalRenumberer
{
    private const string SchemeNode = "USER.Terminal.Numbering.Scheme";

    /// <summary><c>SetScheme(i)</c> leaves the last used scheme as it is, <c>GetName()</c> is the internal name (EPLAN XML docs, B9).</summary>
    public IReadOnlyList<string> Schemes()
    {
        MainThread.Assert();
        using var setting = new SchemeSetting();
        setting.Init(SchemeNode);
        var names = new List<string>();
        for (int i = 0; i < setting.GetCount(); i++)
        {
            setting.SetScheme(i);
            names.Add(setting.GetName() ?? string.Empty);
        }

        return names;
    }

    public IReadOnlyList<PlacedFunction>? ReadTerminals(string projectPath, string? strip)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        return Terminals(finder, strip) is { } terminals
            ? [.. terminals.Select(t => new PlacedFunction { Name = t.Name ?? string.Empty, IsMain = t.IsMainTerminal, Page = t.Page?.Name, Id = t.ToStringIdentifier() })]
            : null;
    }

    /// <summary>
    /// S21's arguments (tool spec 5): in the strip's order, every terminal of the strips of the given ones
    /// (<c>AllSelectedTerminalStrips</c>, XML), N, PE and SH terminals numbered, each terminal of a group on its own,
    /// letters kept, no prefix or suffix, no sort change, no gap filling, PLC terminals left alone; only designations with
    /// '?' when asked (XML; not verified, the sample project has none). EPLAN's InvalidScheme names the schemes it has;
    /// other EPLAN failures go back as EPLAN_OPERATION_FAILED with EPLAN's message.
    /// </summary>
    public void Renumber(string projectPath, IReadOnlyList<string>? strips, bool onlyUnnumbered, string scheme, int start, int increment)
    {
        MainThread.Assert();
        using var finder = new DMObjectsFinder(EplanProjects.ByPath(projectPath));
        Function[] terminals = strips is null
            ? [.. Terminals(finder, null)!.Cast<Function>()]
            : [.. strips.SelectMany(strip => Terminals(finder, strip) ?? throw new InvalidOperationException("A strip the operation resolved is gone.")).Cast<Function>()];
        if (terminals.Length == 0)
        {
            return;
        }

        using var renumber = new EplanRenumber();
        try
        {
            renumber.Terminals(
                terminals,
                TerminalEnums.TerminalsSequence.Level,
                TerminalEnums.Extent.AllSelectedTerminalStrips,
                TerminalEnums.TerminalsPotential.Numbering,
                TerminalEnums.TerminalsPotential.Numbering,
                TerminalEnums.TerminalsPotential.Numbering,
                TerminalEnums.MultipleTerminals.NumberIndividually,
                TerminalEnums.TerminalsWithAlphabeticalCharacters.DontModify,
                scheme,
                start,
                increment,
                string.Empty,
                string.Empty,
                false,
                false,
                onlyUnnumbered,
                false);
        }
        catch (InvalidScheme ex)
        {
            throw SchemeChoice.Refused(scheme, ex.Message, "terminal", ex);
        }
        catch (Exception ex) when (ex is BaseException or ApplicationException or ArgumentException && new EplanExceptionMapper().Map(ex) is null)
        {
            var failed = new BridgeErrorException(
                ErrorReason.EplanOperationFailed,
                "EPLAN could not number the terminals, so nothing was numbered. metadata.eplanMessage has EPLAN's message.",
                ex);
            failed.Error.Metadata = new Dictionary<string, string> { ["eplanMessage"] = EplanConnectionGenerator.ForModel(ex.Message) };
            throw failed;
        }
    }

    /// <summary>
    /// Every terminal, or those of every strip named exactly <paramref name="strip"/>: GetTerminalStrips returns some
    /// strips twice with the same terminals (A24), so each terminal once (StorableObject overrides Equals, as the
    /// connection reader relies on); null without such a strip.
    /// </summary>
    private static Terminal[]? Terminals(DMObjectsFinder finder, string? strip)
    {
        if (strip is null)
        {
            return finder.GetTerminals(null);
        }

        TerminalStrip[] named = [.. finder.GetTerminalStrips(null).Where(s => string.Equals(s.Name, strip, StringComparison.Ordinal))];
        return named.Length == 0 ? null : [.. named.SelectMany(s => s.Terminals).Distinct()];
    }
}
