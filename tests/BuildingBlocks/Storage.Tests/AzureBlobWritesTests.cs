using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Azure;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage.Tests;

/// <summary>
/// AzureBlobWrites creates a blob in the module's own container and never overwrites one (#50, docs/adr/0002): it puts
/// the blob with <c>If-None-Match: *</c> and the content type given, a blob already under the name (409
/// <c>BlobAlreadyExists</c>, or 412 <c>ConditionNotMet</c> for the condition) is <see cref="BlobWriteOutcome.AlreadyThere"/>,
/// and any other failure is thrown. A name or a content type that is not one is refused before any request is made. The
/// storage service is a local endpoint that answers every request with the status and error code the test chooses, and
/// records what it was sent.
/// </summary>
public sealed class AzureBlobWritesTests : IDisposable
{
    private const string Account = "probeaccount";

    private const string Container = "certification";

    private static readonly BlobName Name = BlobName.From("certificates/0193f1a2-7c4e-7a10-9d2b-1f3e5a7c9b0d.pdf");

    private readonly RecordingBlobService service = new();

    [Fact]
    public async Task A_new_blob_is_created_with_the_content_type_given_and_only_if_none_lies_there()
    {
        service.AnswerWith(HttpStatusCode.Created, errorCode: null);
        var content = "%PDF-1.7"u8.ToArray();

        var outcome = await Writes().CreateAsync(Name, BinaryData.FromBytes(content), "application/pdf", TestContext.Current.CancellationToken);

        outcome.ShouldBe(BlobWriteOutcome.Created);
        var put = service.Requests.ShouldHaveSingleItem();
        put.Method.ShouldBe("PUT");
        put.Path.ShouldBe($"/{Account}/{Container}/{Name.Value}");
        put.IfNoneMatch.ShouldBe("*", "a blob already under the name is never overwritten");
        put.BlobContentType.ShouldBe("application/pdf");
        put.Body.ShouldBe(content);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, "BlobAlreadyExists")]
    [InlineData(HttpStatusCode.PreconditionFailed, "ConditionNotMet")]
    public async Task A_blob_already_under_the_name_is_already_there(HttpStatusCode status, string errorCode)
    {
        service.AnswerWith(status, errorCode);

        var outcome = await Writes().CreateAsync(Name, BinaryData.FromBytes("%PDF-1.7"u8.ToArray()), "application/pdf", TestContext.Current.CancellationToken);

        outcome.ShouldBe(BlobWriteOutcome.AlreadyThere);
        service.Requests.Count.ShouldBe(1, "the storage service said so; nothing is written again");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "ContainerNotFound")]
    [InlineData(HttpStatusCode.Forbidden, "AuthorizationPermissionMismatch")]
    [InlineData(HttpStatusCode.Conflict, "LeaseIdMissing")]
    [InlineData(HttpStatusCode.PreconditionFailed, "LeaseIdMismatchWithBlobOperation")]
    public async Task Any_other_failure_is_thrown(HttpStatusCode status, string errorCode)
    {
        service.AnswerWith(status, errorCode);

        var thrown = await Should.ThrowAsync<RequestFailedException>(() =>
            Writes().CreateAsync(Name, BinaryData.FromBytes("%PDF-1.7"u8.ToArray()), "application/pdf", TestContext.Current.CancellationToken));

        thrown.Status.ShouldBe((int)status);
        thrown.ErrorCode.ShouldBe(errorCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("application/pdf; charset=utf-8")]
    [InlineData("Application/PDF")]
    [InlineData("application/pdf\r\nx-ms-meta-a: b")]
    [InlineData("pdf")]
    public async Task A_content_type_that_is_not_one_bare_type_is_refused_before_any_request(string contentType)
    {
        await Should.ThrowAsync<GuardException>(() =>
            Writes().CreateAsync(Name, BinaryData.FromBytes("%PDF-1.7"u8.ToArray()), contentType, TestContext.Current.CancellationToken));

        service.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task No_name_no_content_or_no_content_type_is_refused_before_any_request()
    {
        var content = BinaryData.FromBytes("%PDF-1.7"u8.ToArray());

        await Should.ThrowAsync<GuardException>(() => Writes().CreateAsync(null!, content, "application/pdf", TestContext.Current.CancellationToken));
        await Should.ThrowAsync<GuardException>(() => Writes().CreateAsync(Name, null!, "application/pdf", TestContext.Current.CancellationToken));
        await Should.ThrowAsync<GuardException>(() => Writes().CreateAsync(Name, content, null!, TestContext.Current.CancellationToken));

        service.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Without_a_connection_string_it_fails_when_a_blob_is_first_written_naming_it()
    {
        var writes = new AzureBlobWrites(new ConfigurationBuilder().Build());

        var failure = await Should.ThrowAsync<InvalidOperationException>(() =>
            writes.CreateAsync(Name, BinaryData.FromBytes("%PDF-1.7"u8.ToArray()), "application/pdf", TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("ConnectionStrings:blobs");
    }

    public void Dispose() => service.Dispose();

    private AzureBlobWrites Writes()
    {
        var connectionString = $"DefaultEndpointsProtocol=http;AccountName={Account};AccountKey={Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))};BlobEndpoint={service.Endpoint}{Account};ContainerName={Container}";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:blobs"] = connectionString })
            .Build();
        return new AzureBlobWrites(configuration);
    }

    /// <summary>What the blob endpoint was sent: the method, the path, the condition, the blob's content type and the bytes.</summary>
    private sealed record SentRequest(string Method, string Path, string? IfNoneMatch, string? BlobContentType, byte[] Body);

    /// <summary>A blob endpoint on the loopback interface that answers every request with one status and error code.</summary>
    private sealed class RecordingBlobService : IDisposable
    {
        private readonly HttpListener listener = new();

        private readonly List<SentRequest> requests = [];

        private HttpStatusCode status = HttpStatusCode.Created;

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

                using var body = new MemoryStream();
                await context.Request.InputStream.CopyToAsync(body);
                lock (requests)
                {
                    requests.Add(new SentRequest(
                        context.Request.HttpMethod,
                        context.Request.Url?.AbsolutePath ?? string.Empty,
                        context.Request.Headers["If-None-Match"],
                        context.Request.Headers["x-ms-blob-content-type"],
                        body.ToArray()));
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
                    response.Headers["ETag"] = "\"0x8DCB1A2B3C4D5E6\"";
                    response.Headers["Last-Modified"] = "Fri, 20 Nov 2026 10:00:00 GMT";
                    response.Headers["x-ms-request-server-encrypted"] = "true";
                }

                response.Close();
            }
        }
    }
}
