using Eplan.EplApi.ApplicationFramework;

namespace EplanMcp.Large.ConnectionHelper;

public sealed class SetupAddIn : IEplAddIn
{
    public bool OnRegister(ref bool bLoadOnStart) { bLoadOnStart = false; return true; }
    public bool OnUnregister() => true;
    public bool OnInit() => true;
    public bool OnInitGui() => true;
    public bool OnExit() => true;
}
