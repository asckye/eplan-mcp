using Eplan.EplApi.ApplicationFramework;

namespace EplanMcp.Spikes.S37;

/// <summary>Unsigned, separately registered scratch experiment, loaded on first action invocation (S32).</summary>
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
