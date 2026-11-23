using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage.Tests;

/// <summary>
/// AzureBlobCopies copies only from the module's own storage account: a source whose scheme, host or port differs from the
/// blob service's endpoint is refused before any request is made, so a message cannot make the module fetch from an
/// arbitrary host. The blob service is a local endpoint that counts what it is asked.
/// </summary>
public sealed class AzureBlobCopiesTests : IDisposable
{
    private const string Account = "probeaccount";

    private const string Container = "learning";

    private static readonly DateTimeOffset Noon = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private static readonly BlobName Target = BlobName.From("course-versions/0193a1b2-0000-7000-8000-000000000001/chapters/0193a1b2-0000-7000-8000-000000000002.pdf");

    private readonly CountingBlobService service = new();

    [Fact]
    public async Task A_source_on_another_host_is_refused()
    {
        var source = new BlobUrl(new Uri($"http://elsewhere.example:{service.Endpoint.Port}/{Account}/editorial/handout.pdf?sp=r&sig=fake"), Noon.AddMinutes(15));

        await Should.ThrowAsync<GuardException>(() => Copies().CopyFromAsync(source, Target, TestContext.Current.CancellationToken));

        service.Requests.ShouldBe(0, "a source outside the account is refused before the storage service is asked");
    }

    [Theory]
    [InlineData("https", 0)]
    [InlineData("http", 1)]
    public async Task A_source_on_another_port_or_scheme_is_refused(string scheme, int portOffset)
    {
        var source = new BlobUrl(new Uri($"{scheme}://{service.Endpoint.Host}:{service.Endpoint.Port + portOffset}/{Account}/editorial/handout.pdf?sp=r&sig=fake"), Noon.AddMinutes(15));

        await Should.ThrowAsync<GuardException>(() => Copies().CopyFromAsync(source, Target, TestContext.Current.CancellationToken));

        service.Requests.ShouldBe(0, "a source outside the account is refused before the storage service is asked");
    }

    [Fact]
    public async Task A_missing_connection_string_fails_when_the_first_copy_is_made_and_names_it()
    {
        var copies = new AzureBlobCopies(new ConfigurationBuilder().Build());
        var source = new BlobUrl(new Uri($"http://127.0.0.1:10000/{Account}/editorial/handout.pdf?sp=r&sig=fake"), Noon.AddMinutes(15));

        var failure = await Should.ThrowAsync<InvalidOperationException>(() => copies.CopyFromAsync(source, Target, TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("ConnectionStrings:blobs");
    }

    public void Dispose() => service.Dispose();

    private AzureBlobCopies Copies()
    {
        var connectionString = $"DefaultEndpointsProtocol=http;AccountName={Account};AccountKey={Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))};BlobEndpoint={service.Endpoint}{Account};ContainerName={Container}";
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:blobs"] = connectionString })
            .Build();
        return new AzureBlobCopies(configuration);
    }

    /// <summary>A blob endpoint on the loopback interface that counts every request and refuses it.</summary>
    private sealed class CountingBlobService : IDisposable
    {
        private readonly HttpListener listener = new();

        private int requests;

        public CountingBlobService()
        {
            Endpoint = new Uri($"http://127.0.0.1:{FreePort()}/");
            listener.Prefixes.Add(Endpoint.AbsoluteUri);
            listener.Start();
            _ = Task.Run(AnswerAsync);
        }

        public Uri Endpoint { get; }

        public int Requests => Volatile.Read(ref requests);

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
                context.Response.StatusCode = (int)HttpStatusCode.Forbidden;
                context.Response.Headers["x-ms-error-code"] = "AuthorizationFailure";
                context.Response.Close();
            }
        }
    }
}
