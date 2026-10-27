namespace EplanMcp.Host.Tools;

internal sealed record AssetReviewResult(string Asset, string Decision, string ReviewDigest, string? Rule);
