using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Azure;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage.Tests;

/// <summary>
/// What AzureStoredBlobs makes of a 404 (or, reading a blob's start, a 416) from the storage service: only
/// <c>BlobNotFound</c> says nothing lies under the name, and a range the service cannot satisfy says the blob is empty.
/// Any other 404, a missing container first, says the module's storage is not what its configuration names, and is
/// thrown rather than read as an upload that has not arrived yet. The storage service is a local endpoint that answers
/// every request with the error the test chooses, as the service does: a status and the <c>x-ms-error-code</c> header.
/// Reading a blob's start reads the version whose properties were checked: the ranged read carries <c>If-Match</c> with
/// the ETag <c>FindAsync</c> saw, and a blob changed in between (412 <c>ConditionNotMet</c>) answers no bytes, which the
/// module refuses as it refuses a file that does not start as its kind does. Published, it reads the module's container
/// keyless, under the service's own managed identity: the <c>blobs</c> connection string has the account's endpoint and
/// the container but no key (docs/adr/0002).
/// </summary>
public sealed class AzureStoredBlobsTests : IDisposable
{
    private const string Account = "probeaccount";

    private const string Container = "editorial";

    /// <summary>The ETag the storage service reports for the blob, as Azure writes one: quoted.</summary>
    private const string BlobETag = "\"0x8DCB1A2B3C4D5E6\"";

    private readonly FailingBlobService service = new();

    [Fact]
    public async Task A_missing_blob_is_found_absent()
    {
        service.AnswerWith(HttpStatusCode.NotFound, "BlobNotFound");

        var found = await Blobs().FindAsync(BlobName.From("course-versions/handout.pdf"), TestContext.Current.CancellationToken);

        found.HasValue.ShouldBeFalse();
        service.Requests.ShouldBe(1, "the storage service was asked, so absence is its answer, not the reader's blindness");
    }

    [Fact]
    public async Task A_missing_container_is_thrown_not_found_absent()
    {
        service.AnswerWith(HttpStatusCode.NotFound, "ContainerNotFound");

        var thrown = await Should.ThrowAsync<RequestFailedException>(() =>
            Blobs().FindAsync(BlobName.From("course-versions/handout.pdf"), TestContext.Current.CancellationToken));

        thrown.Status.ShouldBe(404);
        thrown.ErrorCode.ShouldBe("ContainerNotFound");
    }

    [Fact]
    public async Task A_404_with_another_error_code_is_thrown_not_found_absent()
    {
        service.AnswerWith(HttpStatusCode.NotFound, "ResourceNotFound");

        var thrown = await Should.ThrowAsync<RequestFailedException>(() =>
            Blobs().FindAsync(BlobName.From("course-versions/handout.pdf"), TestContext.Current.CancellationToken));

        thrown.ErrorCode.ShouldBe("ResourceNotFound");
    }

    [Fact]
    public async Task The_start_of_a_missing_blob_is_no_bytes()
    {
        service.AnswerWith(HttpStatusCode.NotFound, "BlobNotFound");

        var start = await Blobs().ReadStartAsync(Found(), 8, TestContext.Current.CancellationToken);

        start.Length.ShouldBe(0);
        service.Requests.ShouldBe(1, "the storage service was asked, so no bytes is its answer, not the reader's blindness");
    }

    [Fact]
    public async Task The_start_of_an_empty_blob_whose_range_cannot_be_satisfied_is_no_bytes()
    {
        service.AnswerWith(HttpStatusCode.RequestedRangeNotSatisfiable, "InvalidRange");

        var start = await Blobs().ReadStartAsync(Found(), 8, TestContext.Current.CancellationToken);

        start.Length.ShouldBe(0);
        service.Requests.ShouldBe(1);
    }

    [Fact]
    public async Task Reading_the_start_of_a_blob_in_a_missing_container_is_thrown_not_read_as_no_bytes()
    {
        service.AnswerWith(HttpStatusCode.NotFound, "ContainerNotFound");

        var thrown = await Should.ThrowAsync<RequestFailedException>(() =>
            Blobs().ReadStartAsync(Found(), 8, TestContext.Current.CancellationToken));

        thrown.Status.ShouldBe(404);
        thrown.ErrorCode.ShouldBe("ContainerNotFound");
    }

    [Fact]
    public async Task The_start_of_a_blob_is_read_from_the_version_whose_properties_were_found()
    {
        service.Hold(BlobETag, "%PDF-1.7"u8.ToArray());
        var blobs = Blobs();

        var found = await blobs.FindAsync(BlobName.From("course-versions/handout.pdf"), TestContext.Current.CancellationToken);
        found.HasValue.ShouldBeTrue("the service holds the blob");
        var start = await blobs.ReadStartAsync(found.Value, 8, TestContext.Current.CancellationToken);

        start.ToArray().ShouldBe("%PDF-1.7"u8.ToArray());
        service.IfMatchOfReads.ShouldBe([BlobETag], "the bytes checked must be the bytes of the blob whose properties were checked, not of one put after it");
    }

    [Fact]
    public async Task The_start_of_a_blob_changed_after_it_was_found_is_no_bytes()
    {
        service.Hold(BlobETag, "%PDF-1.7"u8.ToArray());
        var blobs = Blobs();

        var found = await blobs.FindAsync(BlobName.From("course-versions/handout.pdf"), TestContext.Current.CancellationToken);
        found.HasValue.ShouldBeTrue("the service holds the blob");
        // Another PUT under the name between the lookup and the read: the service refuses the read for the old ETag.
        service.AnswerReadsWith(HttpStatusCode.PreconditionFailed, "ConditionNotMet");
        var start = await blobs.ReadStartAsync(found.Value, 8, TestContext.Current.CancellationToken);

        start.Length.ShouldBe(0, "no bytes, so the module refuses the upload rather than check the old version's properties against the new one's bytes");
        service.IfMatchOfReads.ShouldBe([BlobETag]);
    }

    [Fact]
    public async Task A_blob_is_found_with_the_content_disposition_and_encoding_its_uploader_set()
    {
        service.Hold(BlobETag, "%PDF-1.7"u8.ToArray(), contentDisposition: "attachment; filename=handout.html", contentEncoding: "gzip");

        var found = await Blobs().FindAsync(BlobName.From("course-versions/handout.pdf"), TestContext.Current.CancellationToken);

        found.HasValue.ShouldBeTrue("the service holds the blob");
        found.Value.ContentDisposition.ShouldBe("attachment; filename=handout.html", "a read URL serves it back, so the module must see it to refuse it");
        found.Value.ContentEncoding.ShouldBe("gzip", "a read URL serves it back, so the module must see it to refuse it");
    }

    [Fact]
    public async Task A_blob_uploaded_without_a_content_disposition_or_encoding_is_found_without_either()
    {
        service.Hold(BlobETag, "%PDF-1.7"u8.ToArray());

        var found = await Blobs().FindAsync(BlobName.From("course-versions/handout.pdf"), TestContext.Current.CancellationToken);

        found.HasValue.ShouldBeTrue("the service holds the blob");
        found.Value.ContentDisposition.ShouldBeNull();
        found.Value.ContentEncoding.ShouldBeNull();
    }

    [Fact]
    public async Task Without_an_account_key_or_a_managed_identity_it_fails_when_a_blob_is_first_looked_up_naming_both()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:blobs"] = "Endpoint=https://probeaccount.blob.core.windows.net/;ContainerName=editorial" })
            .Build();
        var blobs = new AzureStoredBlobs(configuration);

        var failure = await Should.ThrowAsync<InvalidOperationException>(() =>
            blobs.FindAsync(BlobName.From("handout.pdf"), TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("AccountKey");
        failure.Message.ShouldContain("AZURE_CLIENT_ID");
    }

    public void Dispose() => service.Dispose();

    /// <summary>A blob as FindAsync would have reported it, for a read that fails before any property is checked.</summary>
    private static StoredBlob Found() =>
        new(BlobName.From("course-versions/handout.pdf"), "application/pdf", 300, IsBlockBlob: true, ETag: BlobETag);

    private AzureStoredBlobs Blobs()
    {
        var connectionString = $"DefaultEndpointsProtocol=http;AccountName={Account};AccountKey={Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))};BlobEndpoint={service.Endpoint}{Account};ContainerName={Container}";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:blobs"] = connectionString })
            .Build();
        return new AzureStoredBlobs(configuration);
    }

    /// <summary>
    /// A blob endpoint on the loopback interface that answers every request with one status and error code, or, holding a
    /// blob, its properties to a HEAD and its bytes to a ranged GET until told to answer reads otherwise. It records the
    /// <c>If-Match</c> of every GET.
    /// </summary>
    private sealed class FailingBlobService : IDisposable
    {
        private readonly HttpListener listener = new();

        private HttpStatusCode status = HttpStatusCode.NotFound;

        private string errorCode = "BlobNotFound";

        private readonly List<string?> ifMatchOfReads = [];

        private int requests;

        private string? heldETag;

        private byte[] heldContent = [];

        private string? heldContentDisposition;

        private string? heldContentEncoding;

        private HttpStatusCode? readStatus;

        private string readErrorCode = string.Empty;

        public FailingBlobService()
        {
            Endpoint = new Uri($"http://127.0.0.1:{FreePort()}/");
            listener.Prefixes.Add(Endpoint.AbsoluteUri);
            listener.Start();
            _ = Task.Run(AnswerAsync);
        }

        public Uri Endpoint { get; }

        public int Requests => Volatile.Read(ref requests);

        public IReadOnlyList<string?> IfMatchOfReads
        {
            get
            {
                lock (ifMatchOfReads)
                {
                    return [.. ifMatchOfReads];
                }
            }
        }

        public void Hold(string etag, byte[] content, string? contentDisposition = null, string? contentEncoding = null)
        {
            heldETag = etag;
            heldContent = content;
            heldContentDisposition = contentDisposition;
            heldContentEncoding = contentEncoding;
        }

        public void AnswerReadsWith(HttpStatusCode status, string errorCode)
        {
            readStatus = status;
            readErrorCode = errorCode;
        }

        public void AnswerWith(HttpStatusCode status, string errorCode)
        {
            this.status = status;
            this.errorCode = errorCode;
        }

        public void Dispose() => listener.Close();

        private static int FreePort()
        {
            using var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            return ((IPEndPoint)probe.LocalEndpoint).Port;
        }

        private async Task AnswerAsync()
        {
            while (listener.IsListening)
            {
                HttpListenerContext context;
                try
                {
                    context = await listener.GetContextAsync();
                }
                catch (HttpListenerException)
                {
                    return;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }

                Interlocked.Increment(ref requests);
                var response = context.Response;
                response.Headers["x-ms-request-id"] = Guid.CreateVersion7().ToString();
                response.Headers["x-ms-version"] = "2025-01-05";
                var isRead = string.Equals(context.Request.HttpMethod, "GET", StringComparison.Ordinal);
                if (isRead)
                {
                    lock (ifMatchOfReads)
                    {
                        ifMatchOfReads.Add(context.Request.Headers["If-Match"]);
                    }
                }

                if (heldETag is null || (isRead && readStatus is not null))
                {
                    response.StatusCode = (int)(isRead && readStatus is { } refused ? refused : status);
                    response.Headers["x-ms-error-code"] = isRead && readStatus is not null ? readErrorCode : errorCode;
                    response.Close();
                    continue;
                }

                response.Headers["ETag"] = heldETag;
                response.Headers["Last-Modified"] = "Fri, 20 Nov 2026 10:00:00 GMT";
                response.Headers["x-ms-blob-type"] = "BlockBlob";
                response.ContentType = "application/pdf";
                if (heldContentDisposition is not null)
                {
                    response.Headers["Content-Disposition"] = heldContentDisposition;
                }

                if (heldContentEncoding is not null)
                {
                    response.Headers["Content-Encoding"] = heldContentEncoding;
                }
                if (!isRead)
                {
                    response.StatusCode = (int)HttpStatusCode.OK;
                    response.ContentLength64 = 300;
                    response.Close();
                    continue;
                }

                response.StatusCode = (int)HttpStatusCode.PartialContent;
                response.Headers["Content-Range"] = $"bytes 0-{heldContent.Length - 1}/300";
                response.ContentLength64 = heldContent.Length;
                await response.OutputStream.WriteAsync(heldContent);
                response.Close();
            }
        }
    }
}
