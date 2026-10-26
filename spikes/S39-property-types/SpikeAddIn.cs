using Eplan.EplApi.ApplicationFramework;

namespace EplanMcp.Spikes.S39;

public sealed class SpikeAddIn : IEplAddIn
{
    public bool OnRegister(ref bool bLoadOnStart) { bLoadOnStart = false; return true; }
    public bool OnUnregister() => true;
    public bool OnInit() => true;
    public bool OnInitGui() => true;
    public bool OnExit() => true;
}
