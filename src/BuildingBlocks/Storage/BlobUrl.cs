namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>A signed URL for one blob, and the instant it stops working.</summary>
/// <param name="Url">The blob's URL with its signature; a bearer credential for that blob until <paramref name="ExpiresAt"/>.</param>
/// <param name="ExpiresAt">When the storage service starts refusing the URL.</param>
public sealed record BlobUrl(Uri Url, DateTimeOffset ExpiresAt);
