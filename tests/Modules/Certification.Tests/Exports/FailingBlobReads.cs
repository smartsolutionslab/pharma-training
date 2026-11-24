using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Exports;

/// <summary>
/// Blob storage that fails an export after it has begun (#367): either no blob can be opened, as when storage cannot be
/// reached, or every blob opens and its connection is lost while it is read.
/// </summary>
internal sealed class FailingBlobReads : IBlobReads
{
    private readonly bool failsAtOpen;

    private FailingBlobReads(bool failsAtOpen)
    {
        this.failsAtOpen = failsAtOpen;
    }

    /// <summary>Every blob fails to open.</summary>
    public static FailingBlobReads AtOpen() => new(failsAtOpen: true);

    /// <summary>Every blob opens and then fails after its first bytes.</summary>
    public static FailingBlobReads WhileCopying() => new(failsAtOpen: false);

    public Task<Stream> OpenReadAsync(BlobName name, CancellationToken cancellationToken)
    {
        if (failsAtOpen)
        {
            throw new IOException("The blob storage could not be reached.");
        }

        return Task.FromResult<Stream>(new FailingReadStream());
    }
}
