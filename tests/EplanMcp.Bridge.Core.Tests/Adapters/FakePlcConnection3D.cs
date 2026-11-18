namespace Eplan.EplApi.DataModel.E3D;

internal sealed class Connection3D : Connection
{
    // A11: neither endpoint pin can be read on a 3D connection.
    public override Pin? StartPin { get => throw new NotSupportedException(); set => base.StartPin = value; }
    public override Pin? EndPin { get => throw new NotSupportedException(); set => base.EndPin = value; }
}
