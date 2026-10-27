using System.Runtime.CompilerServices;
using System.Text.Json;
using EplanMcp.Contracts;
using EplanMcp.Host.Bridge;
using ModelContextProtocol.Protocol;

namespace EplanMcp.Host.Tools;

internal static class PageDrawingDiscovery
{
    private static readonly ConditionalWeakTable<IBridgeClient, PropertySchemaPaging> _sessions = new();

    internal static PropertySchemaPaging For(IBridgeClient bridge, TimeProvider clock) => _sessions.GetValue(bridge, _ => new(clock));

    internal static async Task<CallToolResult?> ReadAsync(IBridgeClient bridge, PageDrawingGeometry geometry, PageGetArgs args, PageGetResult result,
        int limit, string? cursor, int budget, CancellationToken cancellationToken)
    {
        try
        {
            BridgeStatus status = await bridge.GetStatusAsync(cancellationToken).ConfigureAwait(false);
            if (status.Problem is { } problem)
            {
                return ToolResults.Error(problem);
            }

            string query = PropertyGetPaging.Hash(JsonSerializer.Serialize(new
            {
                geometry,
                args.Language,
                args.IncludeProperties,
                status.Handshake?.Eplan.ProcessId,
                Instance = status.Instances.FirstOrDefault(e => e.ProcessId == status.Handshake?.Eplan.ProcessId)?.PipeName,
            }, HostJson.Options));
            PropertySchemaPaging paging = For(bridge, TimeProvider.System);
            var violations = new List<string>();
            int offset = paging.Offset(cursor, query, violations);
            if (violations.Count > 0 || offset > geometry.Placements.Count)
            {
                return ToolResults.InvalidArguments(["/drawing_cursor is invalid, expired or stale; omit it to restart drawing discovery."]);
            }

            var drawings = new PageDrawingsResult
            {
                GridSize = geometry.GridSize,
                PageSize = geometry.PageSize,
                PlotFrameSize = geometry.PlotFrameSize,
                BoundarySource = geometry.PageSize is null ? "unknown" : "Page.Size",
                Total = geometry.Placements.Count,
                Items = [.. geometry.Placements.Skip(offset).Take(limit).Select(row => new DrawingPlacement
                {
                    Locator = row.Locator, Kind = row.Kind, Location = row.Location, Bounds = row.Bounds, Parent = row.Parent,
                    Properties = row.Properties.ToDictionary(p => p.Key, p => ResultShaper.CleanFullText(p.Value), StringComparer.Ordinal),
                })],
            };
            int propertyCount = result.Properties?.Count ?? 0;
            bool previouslyTruncated = result.Truncated;
            result.Drawings = drawings;
            void Continue()
            {
                int next = offset + drawings.Items.Count;
                drawings.Truncated = next < drawings.Total;
                drawings.NextCursor = drawings.Truncated ? paging.Encode(next, query) : null;
            }
            Continue();
            result.Truncated = true;
            result.Hint = "Drawing objects may be paged or cut to the size budget; pass drawings.nextCursor as drawing_cursor. Other omitted entries are available with detailed or eplan_device_list.";
            ResultShaper.FitItems(result, result.Properties, budget);
            ResultShaper.FitItems(result, result.Devices, budget);
            while (!ResultShaper.Fits(result, budget) && drawings.Items.Count > 0)
            {
                drawings.Items.RemoveAt(drawings.Items.Count - 1);
                Continue();
            }
            if (!ResultShaper.Fits(result, budget) || drawings.Items.Count == 0 && offset < drawings.Total)
            {
                return ToolResults.InvalidArguments(["/drawing_cursor cannot advance within the response budget; use concise or ask the engineer to shorten oversized drawing text, then restart."]);
            }
            result.Truncated = previouslyTruncated || drawings.Truncated || result.Devices.Count < result.DeviceCount || (result.Properties?.Count ?? 0) < propertyCount;
            if (!result.Truncated)
            {
                result.Hint = null;
            }
            return null;
        }
        catch (BridgeCallException ex)
        {
            return ToolResults.Error(ex.Error);
        }
    }
}
