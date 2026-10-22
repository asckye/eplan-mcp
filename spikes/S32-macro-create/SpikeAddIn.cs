using Eplan.EplApi.ApplicationFramework;

namespace EplanMcp.Spikes.S32;

/// <summary>
/// Minimal add-in entry point. Registered with bLoadOnStart = false, so EPLAN loads the
/// assembly lazily the first time <see cref="SpikeS32Action"/> runs and no restart is needed.
/// </summary>
public sealed class SpikeAddIn : IEplAddIn
{
    public bool OnRegister(ref bool bLoadOnStart)
    {
        bLoadOnStart = false;
        return true;
    }

    public bool OnUnregister() => true;

    public bool OnInit() => true;

    public bool OnInitGui() => true;

    public bool OnExit() => true;
}
