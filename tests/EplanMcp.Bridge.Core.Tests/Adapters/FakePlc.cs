namespace Eplan.EplApi.DataModel.EObjects;

internal sealed class PLC(long oid = 4) : Function(oid)
{
    public bool IsCPU { get; set; }
    private Terminal[] _terminals = [];
    public Terminal[] PLCTerminals { get { CheckNative?.Invoke(); return _terminals; } set { CheckNative?.Invoke(); _terminals = value; } }
}
