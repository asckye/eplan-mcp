using Eplan.EplApi.Base;
using Eplan.EplApi.DataModel;
using Eplan.EplApi.DataModel.E3D;
using Eplan.EplApi.HEServices;
using EplanMcp.Bridge.Core.Operations;
using EplanMcp.Contracts;

namespace EplanMcp.Bridge.Eplan.Adapters;

/// <summary>
/// Reads and routes the 3D connections of one installation space as SampleRouteCheckTests did on the sample (api-notes
/// A141): <c>InstallationSpace.Connection3Ds</c>, path lengths from <c>RoutePoints</c>, and
/// <c>ConnectionService3D.RouteConnections</c>, which gave the sample's lengths again. Main thread.
/// </summary>
internal sealed class EplanConnectionRouter : IConnectionRouter
{
    public IReadOnlyList<RoutedConnection>? Read(string projectPath, string space)
    {
        MainThread.Assert();
        InstallationSpace? target = Space(projectPath, space);
        return target is null ? null : [.. target.Connection3Ds.Select(Describe)];
    }

    public void Route(string projectPath, string space, IReadOnlyCollection<string> devices, bool network)
    {
        MainThread.Assert();
        InstallationSpace target = Space(projectPath, space) ?? throw new InvalidOperationException("No installation space " + space + ".");
        List<Connection3D> selected = [.. target.Connection3Ds.Where(c => devices.Count == 0 || devices.Contains(Device(c, start: true)) || devices.Contains(Device(c, start: false)))];
        if (selected.Count == 0)
        { return; }
        using var service = new ConnectionService3D();
        service.RouteConnections(selected, network ? Connection3D.Enums.RoutingType.RoutingWithNetwork : Connection3D.Enums.RoutingType.RoutingWithoutNetwork);
    }

    private static InstallationSpace? Space(string projectPath, string space) =>
        EplanProjects.ByPath(projectPath).InstallationSpaces.FirstOrDefault(s => EplanLayoutReader.Designation(s) == space);

    private static RoutedConnection Describe(Connection3D connection)
    {
        bool routed = false;
        double length = 0;
        try
        {
            routed = connection.IsRouted;
            PointD3D[] points = connection.RoutePoints ?? [];
            for (int i = 1; i < points.Length; i++)
            {
                double dx = points[i].X - points[i - 1].X, dy = points[i].Y - points[i - 1].Y, dz = points[i].Z - points[i - 1].Z;
                length += Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            }
        }
        catch (NotSupportedException)
        {
            routed = false;
            length = 0;
        }

        return new RoutedConnection
        {
            From = Device(connection, start: true),
            FromPin = Pin(connection, start: true),
            To = Device(connection, start: false),
            ToPin = Pin(connection, start: false),
            Routed = routed,
            LengthMm = Math.Round(length, 1),
        };
    }

    private static string Device(Connection3D connection, bool start) =>
        Read(() => (start ? connection.StartObject : connection.EndObject)?.Name) ?? string.Empty;

    /// <summary>The pin, or where the end is a connection point position (StartPin throws S063219, A141) its index.</summary>
    private static string Pin(Connection3D connection, bool start) =>
        Read(() => (start ? connection.StartPin : connection.EndPin)?.Name)
        ?? Read(() =>
        {
            using ConnectionPointPosition? position = start ? connection.StartConnectionPointPosition : connection.EndConnectionPointPosition;
            return position is null ? null : "#" + position.Index;
        })
        ?? string.Empty;

    private static string? Read(Func<string?> member)
    {
        try
        { return member(); }
        catch (NotSupportedException)
        { return null; }
    }
}
