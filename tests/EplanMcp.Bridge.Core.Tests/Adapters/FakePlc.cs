namespace Eplan.EplApi.DataModel.EObjects;

internal sealed class PLC() : Function(4)
{
    public bool IsCPU { get; set; }
    public Terminal[] PLCTerminals { get; set; } = [];
    public FunctionPropertyList Properties { get; } = new();
}
