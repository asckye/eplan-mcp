namespace Eplan.EplApi.DataModel.Graphics;

internal sealed class PlaceHolder(long oid) : Placement(oid, 115)
{
    public string Name { get; set; } = "Starter";
    public string[] VariableNames { get; set; } = ["Text"];
    public StorableObject[] AssignedObjects { get; set; } = [];
    public string[] Records { get; set; } = ["Run"];
    public Dictionary<(string Record, string Variable), string> Values { get; } = [];
    public Dictionary<(StorableObject Object, int Id, int Index), string> Bindings { get; } = [];
    public int NumberOfRecords => Records.Length;
    public string get_NameOfRecord(int i) => Records[i];
    public Base.MultiLangString GetValue(string record, string variable) => new(Values[(record, variable)]);
    public Base.MultiLangString? GetPropertyEntry(StorableObject obj, AnyPropertyId id) => Bindings.TryGetValue((obj, id.AsInt, id.Index), out string? expression) ? new(expression) : null;
    public Base.MultiLangString? GetProjectPropertyEntry(AnyPropertyId id)
    {
        string? expression = Bindings.FirstOrDefault(b => b.Key.Object is Project && b.Key.Id == id.AsInt && b.Key.Index == id.Index).Value;
        return expression is null ? null : new(expression);
    }
}
