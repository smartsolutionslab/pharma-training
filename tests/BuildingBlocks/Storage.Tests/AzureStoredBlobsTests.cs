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
/// </summary>
public sealed class AzureStoredBlobsTests : IDisposable
{
    private const string Account = "probeaccount";

    private const string Container = "editorial";

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

        var start = await Blobs().ReadStartAsync(BlobName.From("course-versions/handout.pdf"), 8, TestContext.Current.CancellationToken);

        start.Length.ShouldBe(0);
        service.Requests.ShouldBe(1, "the storage service was asked, so no bytes is its answer, not the reader's blindness");
    }

    [Fact]
    public async Task The_start_of_an_empty_blob_whose_range_cannot_be_satisfied_is_no_bytes()
    {
        service.AnswerWith(HttpStatusCode.RequestedRangeNotSatisfiable, "InvalidRange");

        var start = await Blobs().ReadStartAsync(BlobName.From("course-versions/handout.pdf"), 8, TestContext.Current.CancellationToken);

        start.Length.ShouldBe(0);
        service.Requests.ShouldBe(1);
    }

    [Fact]
    public async Task Reading_the_start_of_a_blob_in_a_missing_container_is_thrown_not_read_as_no_bytes()
    {
        service.AnswerWith(HttpStatusCode.NotFound, "ContainerNotFound");

        var thrown = await Should.ThrowAsync<RequestFailedException>(() =>
            Blobs().ReadStartAsync(BlobName.From("course-versions/handout.pdf"), 8, TestContext.Current.CancellationToken));

        thrown.Status.ShouldBe(404);
        thrown.ErrorCode.ShouldBe("ContainerNotFound");
    }

    public void Dispose() => service.Dispose();

    private AzureStoredBlobs Blobs()
    {
        var connectionString = $"DefaultEndpointsProtocol=http;AccountName={Account};AccountKey={Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))};BlobEndpoint={service.Endpoint}{Account};ContainerName={Container}";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:blobs"] = connectionString })
            .Build();
        return new AzureStoredBlobs(configuration);
    }

    /// <summary>A blob endpoint on the loopback interface that answers every request with one status and error code.</summary>
    private sealed class FailingBlobService : IDisposable
    {
        private readonly HttpListener listener = new();

        private HttpStatusCode status = HttpStatusCode.NotFound;

        private string errorCode = "BlobNotFound";

        private int requests;

        public FailingBlobService()
        {
            Endpoint = new Uri($"http://127.0.0.1:{FreePort()}/");
            listener.Prefixes.Add(Endpoint.AbsoluteUri);
            listener.Start();
            _ = Task.Run(AnswerAsync);
        }

        public Uri Endpoint { get; }

        public int Requests => Volatile.Read(ref requests);

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
                context.Response.StatusCode = (int)status;
                context.Response.Headers["x-ms-error-code"] = errorCode;
                context.Response.Headers["x-ms-request-id"] = Guid.CreateVersion7().ToString();
                context.Response.Headers["x-ms-version"] = "2025-01-05";
                context.Response.Close();
            }
        }
    }
}
