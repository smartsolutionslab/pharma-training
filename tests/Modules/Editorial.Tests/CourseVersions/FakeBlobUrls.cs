using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// Signs nothing: answers a URL that names the blob, expiring when the lifetime from the clock's now ends, and keeps
/// what it was asked to sign, so a test sees the name, lifetime and read headers the handler chose.
/// </summary>
internal sealed class FakeBlobUrls(TimeProvider time) : IBlobUrls
{
    public List<(BlobName Name, BlobUrlLifetime Lifetime)> Uploads { get; } = [];

    public List<(BlobName Name, BlobUrlLifetime Lifetime, BlobReadHeaders Headers)> Reads { get; } = [];

    public Task<BlobUrl> CreateUploadUrlAsync(BlobName name, BlobUrlLifetime lifetime, CancellationToken cancellationToken)
    {
        Uploads.Add((name, lifetime));
        return Task.FromResult(UrlFor(name, lifetime, "c"));
    }

    public Task<BlobUrl> CreateReadUrlAsync(BlobName name, BlobUrlLifetime lifetime, BlobReadHeaders headers, CancellationToken cancellationToken)
    {
        Reads.Add((name, lifetime, headers));
        return Task.FromResult(UrlFor(name, lifetime, "r"));
    }

    public BlobUrl UrlFor(BlobName name, BlobUrlLifetime lifetime, string permission) =>
        new(new Uri($"https://blobs.example/editorial/{name.Value}?sp={permission}&sig=fake"), time.GetUtcNow().Add(lifetime.Value));
}
