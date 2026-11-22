using System.Text;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>The module's blob container in memory: what a test uploaded, and how often a handler looked.</summary>
internal sealed class InMemoryStoredBlobs : IStoredBlobs
{
    /// <summary>The leading bytes of a PDF: the header every PDF file starts with.</summary>
    public static readonly byte[] PdfStart = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj << /Type /Catalog >> endobj\n");

    /// <summary>The leading bytes of an MP4: a box of 24 bytes whose type, bytes 4 to 7, is <c>ftyp</c>.</summary>
    public static readonly byte[] Mp4Start = [0, 0, 0, 24, .. Encoding.ASCII.GetBytes("ftypisom"), 0, 0, 2, 0, .. Encoding.ASCII.GetBytes("isommp42")];

    /// <summary>What an HTML page starts with: neither a PDF nor an MP4.</summary>
    public static readonly byte[] HtmlStart = Encoding.UTF8.GetBytes("<html><body>Kein PDF</body></html>");

    private readonly Dictionary<BlobName, StoredBlob> blobs = [];

    private readonly Dictionary<BlobName, byte[]> contents = [];

    public int Lookups { get; private set; }

    /// <summary>How often a handler read a blob's leading bytes.</summary>
    public int StartReads { get; private set; }

    /// <summary>
    /// Stores a blob as the browser would have uploaded it: a block blob, put directly, unless told otherwise. Without
    /// <paramref name="content"/> its bytes start as a file of the declared content type does: a PDF's header for
    /// <c>application/pdf</c>, an MP4's <c>ftyp</c> box for <c>video/mp4</c>, an HTML page for any other.
    /// </summary>
    public void Upload(string name, string contentType, long length, bool isBlockBlob = true, bool isCopy = false, byte[]? content = null)
    {
        var blobName = BlobName.From(name);
        blobs[blobName] = new StoredBlob(blobName, contentType, length, isBlockBlob, isCopy);
        contents[blobName] = content ?? StartOf(contentType);
    }

    public Task<Option<StoredBlob>> FindAsync(BlobName name, CancellationToken cancellationToken)
    {
        Lookups++;
        return Task.FromResult(blobs.TryGetValue(name, out var blob) ? Option<StoredBlob>.Some(blob) : Option<StoredBlob>.None);
    }

    /// <summary>
    /// The first <paramref name="count"/> bytes of the blob, or all of them when it is shorter: the ranged read the module
    /// asks for to check what was uploaded rather than what the uploader declared. It is not on <see cref="IStoredBlobs"/>
    /// yet; once the interface declares it, this implements it.
    /// </summary>
    public Task<ReadOnlyMemory<byte>> ReadStartAsync(BlobName name, int count, CancellationToken cancellationToken)
    {
        StartReads++;
        var content = contents.TryGetValue(name, out var bytes) ? bytes : [];
        return Task.FromResult(new ReadOnlyMemory<byte>(content, 0, Math.Min(count, content.Length)));
    }

    private static byte[] StartOf(string contentType)
    {
        var mediaType = contentType.Split(';', 2)[0].Trim();
        if (string.Equals(mediaType, "application/pdf", StringComparison.OrdinalIgnoreCase))
        {
            return PdfStart;
        }

        if (string.Equals(mediaType, "video/mp4", StringComparison.OrdinalIgnoreCase))
        {
            return Mp4Start;
        }

        return HtmlStart;
    }
}
