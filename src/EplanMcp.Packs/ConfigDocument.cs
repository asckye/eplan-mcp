using System.Text.Json;

namespace EplanMcp.Packs;

/// <summary>A resolved configuration spec with its exact resource id and original file hash.</summary>
public sealed record ConfigDocument(string Resource, string Sha256, JsonElement Spec);
