namespace EplanMcp.Tests;

internal sealed class ConnectionCreateLayout
{
    internal double[] Position { get; }
    internal (double[] Position, string[] Directions)? Corner { get; }
    internal List<(double[] Pin, string Facing)> Targets { get; } = [];

    internal ConnectionCreateLayout(double grid, string[] directions)
    {
        Position = [30 * grid, 40 * grid];
        string bend = directions.Length == 3 ? directions.Contains("Up", StringComparer.Ordinal) && directions.Contains("Down", StringComparer.Ordinal) ? "Up" : "Left" : "";
        if (bend != "")
        {
            Corner = bend == "Up"
                ? ([Position[0], Position[1] + 8 * grid], ["Down", "Right"])
                : ([Position[0] - 8 * grid, Position[1]], ["Right", "Up"]);
        }
        foreach (string direction in directions)
        {
            double dx = direction == "Right" ? 8 * grid : direction == "Left" ? -8 * grid : 0;
            double dy = direction == "Up" ? 8 * grid : direction == "Down" ? -8 * grid : 0;
            string facing = direction switch { "Up" => "Down", "Down" => "Up", "Left" => "Right", "Right" => "Left", _ => throw new InvalidOperationException("Invalid fixture direction.") };
            // An opposite pair facing the central T would autoconnect before it exists (A52).
            if (direction == bend)
            {
                if (bend == "Up")
                { dx += 8 * grid; facing = "Left"; }
                else
                { dy += 8 * grid; facing = "Down"; }
            }
            Targets.Add(([Position[0] + dx, Position[1] + dy], facing));
        }
    }
}
