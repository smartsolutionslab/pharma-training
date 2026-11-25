using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Azure;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage.Tests;

/// <summary>
/// AzureBlobDeletes deletes a blob in the module's own container (#115, an erased person's certificate PDF): it sends
/// Delete Blob for the name, and a blob that is not there (404 <c>BlobNotFound</c>) is success, so a retry deletes what
/// is left. Any other failure, a missing container among them, is thrown, and a missing name is refused before any
/// request is made. The storage service is a local endpoint that answers every request with the status and error code
/// the test chooses, and records what it was sent.
/// </summary>
public sealed class AzureBlobDeletesTests : IDisposable
{
    private const string Account = "probeaccount";

    private const string Container = "certification";

    private static readonly BlobName Name = BlobName.From("certificates/0193f1a2-7c4e-7a10-9d2b-1f3e5a7c9b0d.pdf");

    private readonly RecordingBlobService service = new();

    [Fact]
    public async Task The_blob_is_deleted_by_its_name_in_the_modules_container()
    {
        service.AnswerWith(HttpStatusCode.Accepted, errorCode: null);

        await Deletes().DeleteAsync(Name, TestContext.Current.CancellationToken);

        var delete = service.Requests.ShouldHaveSingleItem();
        delete.Method.ShouldBe("DELETE");
        delete.Path.ShouldBe($"/{Account}/{Container}/{Name.Value}");
    }

    [Fact]
    public async Task A_blob_that_is_not_there_is_success()
    {
        service.AnswerWith(HttpStatusCode.NotFound, "BlobNotFound");

        await Should.NotThrowAsync(() => Deletes().DeleteAsync(Name, TestContext.Current.CancellationToken));

        service.Requests.Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "ContainerNotFound")]
    [InlineData(HttpStatusCode.Forbidden, "AuthorizationPermissionMismatch")]
    [InlineData(HttpStatusCode.Conflict, "LeaseIdMissing")]
    public async Task Any_other_failure_is_thrown(HttpStatusCode status, string errorCode)
    {
        service.AnswerWith(status, errorCode);

        var thrown = await Should.ThrowAsync<RequestFailedException>(() => Deletes().DeleteAsync(Name, TestContext.Current.CancellationToken));

        thrown.Status.ShouldBe((int)status);
        thrown.ErrorCode.ShouldBe(errorCode);
    }

    [Fact]
    public async Task No_name_is_refused_before_any_request()
    {
        await Should.ThrowAsync<GuardException>(() => Deletes().DeleteAsync(null!, TestContext.Current.CancellationToken));

        service.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_connection_string_it_fails_when_a_blob_is_first_deleted_naming_it()
    {
        var deletes = new AzureBlobDeletes(new ConfigurationBuilder().Build());

        var failure = await Should.ThrowAsync<InvalidOperationException>(() => deletes.DeleteAsync(Name, TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("ConnectionStrings:blobs");
    }

    public void Dispose() => service.Dispose();

    private AzureBlobDeletes Deletes()
    {
        var connectionString = $"DefaultEndpointsProtocol=http;AccountName={Account};AccountKey={Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))};BlobEndpoint={service.Endpoint}{Account};ContainerName={Container}";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:blobs"] = connectionString })
            .Build();
        return new AzureBlobDeletes(configuration);
    }

    /// <summary>What the blob endpoint was sent: the method and the path.</summary>
    private sealed record SentRequest(string Method, string Path);

    /// <summary>A blob endpoint on the loopback interface that answers every request with one status and error code.</summary>
    private sealed class RecordingBlobService : IDisposable
    {
        private readonly HttpListener listener = new();

        private readonly List<SentRequest> requests = [];

        private HttpStatusCode status = HttpStatusCode.Accepted;

        private string? errorCode;

        public RecordingBlobService()
        {
            Endpoint = new Uri($"http://127.0.0.1:{FreePort()}/");
            listener.Prefixes.Add(Endpoint.AbsoluteUri);
            listener.Start();
            _ = Task.Run(AnswerAsync);
        }

        public Uri Endpoint { get; }

        public IReadOnlyList<SentRequest> Requests
        {
            get
            {
                lock (requests)
                {
                    return [.. requests];
                }
            }
        }

        public void AnswerWith(HttpStatusCode status, string? errorCode)
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

                lock (requests)
                {
                    requests.Add(new SentRequest(context.Request.HttpMethod, context.Request.Url?.AbsolutePath ?? string.Empty));
                }

                var response = context.Response;
                response.Headers["x-ms-request-id"] = Guid.CreateVersion7().ToString();
                response.Headers["x-ms-version"] = "2025-01-05";
                response.StatusCode = (int)status;
                if (errorCode is not null)
                {
                    response.Headers["x-ms-error-code"] = errorCode;
                }
                else
                {
                    response.Headers["x-ms-delete-type-permanent"] = "true";
                }

                response.Close();
            }
        }
    }
}
