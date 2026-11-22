using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;
using Aspire.Hosting.Testing;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The URLs AzureBlobUrls signs, sent to the storage emulator the AppHost runs: an upload URL creates its own blob and does
/// nothing else, a read URL reads its own blob and does nothing else, neither works once its lifetime ends, and the web
/// app's origin, alone, may use them from a browser. AzureStoredBlobs reads what was uploaded.
/// </summary>
public class BlobStorageTests(AspireFixture aspire)
{
    private const string Storage = "storage";

    private const string WebOrigin = "http://localhost:5173";

    private const string OtherOrigin = "http://other.example";

    private static readonly TimeSpan ResourceTimeout = TimeSpan.FromMinutes(2);

    private static readonly BlobUrlLifetime FiveMinutes = BlobUrlLifetime.From(TimeSpan.FromMinutes(5));

    private static readonly BlobUrlLifetime FifteenMinutes = BlobUrlLifetime.From(TimeSpan.FromMinutes(15));

    private static readonly HttpClient Http = new();

    public static TheoryData<string> BrowserMethods { get; } = ["PUT", "GET"];

    [Fact]
    public async Task Each_modules_container_takes_an_upload_and_serves_it_to_a_read_url()
    {
        var modules = Modules();

        modules.ShouldNotBeEmpty();
        foreach (var module in modules)
        {
            var urls = await UrlsAsync(module, TimeProvider.System.GetUtcNow());
            var name = NewName();
            var content = RandomNumberGenerator.GetBytes(256);

            var upload = await urls.CreateUploadUrlAsync(name, FiveMinutes, TestContext.Current.CancellationToken);
            using var put = await PutAsync(upload.Url, content);
            var read = await urls.CreateReadUrlAsync(name, FiveMinutes, TestContext.Current.CancellationToken);
            using var get = await Http.GetAsync(read.Url, TestContext.Current.CancellationToken);

            put.StatusCode.ShouldBe(HttpStatusCode.Created, module);
            get.StatusCode.ShouldBe(HttpStatusCode.OK, module);
            (await get.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).ShouldBe(content, module);
        }
    }

    [Fact]
    public async Task Each_container_connection_string_names_its_module()
    {
        var modules = Modules();

        modules.ShouldNotBeEmpty();
        foreach (var module in modules)
        {
            var connectionString = await ConnectionStringAsync(module);

            connectionString.ShouldContain($"ContainerName={module}", Case.Sensitive, module);
        }
    }

    [Fact]
    public async Task An_upload_url_puts_no_other_blob()
    {
        var urls = await UrlsAsync("editorial", TimeProvider.System.GetUtcNow());
        var name = NewName();
        var other = NewName();

        var upload = await urls.CreateUploadUrlAsync(name, FiveMinutes, TestContext.Current.CancellationToken);
        using var put = await PutAsync(Elsewhere(upload.Url, name, other), RandomNumberGenerator.GetBytes(16));

        put.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_upload_url_cannot_read_its_blob()
    {
        var urls = await UrlsAsync("editorial", TimeProvider.System.GetUtcNow());
        var name = NewName();

        var upload = await urls.CreateUploadUrlAsync(name, FiveMinutes, TestContext.Current.CancellationToken);
        using var put = await PutAsync(upload.Url, RandomNumberGenerator.GetBytes(16));
        using var get = await Http.GetAsync(upload.Url, TestContext.Current.CancellationToken);

        put.StatusCode.ShouldBe(HttpStatusCode.Created);
        get.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_upload_url_cannot_delete_its_blob()
    {
        var urls = await UrlsAsync("editorial", TimeProvider.System.GetUtcNow());
        var name = NewName();

        var upload = await urls.CreateUploadUrlAsync(name, FiveMinutes, TestContext.Current.CancellationToken);
        using var put = await PutAsync(upload.Url, RandomNumberGenerator.GetBytes(16));
        using var delete = await Http.DeleteAsync(upload.Url, TestContext.Current.CancellationToken);
        var read = await urls.CreateReadUrlAsync(name, FiveMinutes, TestContext.Current.CancellationToken);
        using var get = await Http.GetAsync(read.Url, TestContext.Current.CancellationToken);

        put.StatusCode.ShouldBe(HttpStatusCode.Created);
        delete.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        get.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task An_upload_url_cannot_overwrite_its_blob()
    {
        var urls = await UrlsAsync("editorial", TimeProvider.System.GetUtcNow());
        var name = NewName();
        var first = RandomNumberGenerator.GetBytes(16);

        var upload = await urls.CreateUploadUrlAsync(name, FiveMinutes, TestContext.Current.CancellationToken);
        using var put = await PutAsync(upload.Url, first);
        using var again = await PutAsync(upload.Url, RandomNumberGenerator.GetBytes(16));
        var read = await urls.CreateReadUrlAsync(name, FiveMinutes, TestContext.Current.CancellationToken);
        using var get = await Http.GetAsync(read.Url, TestContext.Current.CancellationToken);

        put.StatusCode.ShouldBe(HttpStatusCode.Created);
        again.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await get.Content.ReadAsByteArrayAsync(TestContext.Current.CancellationToken)).ShouldBe(first);
    }

    [Fact]
    public async Task An_expired_upload_url_is_refused()
    {
        var urls = await UrlsAsync("editorial", TimeProvider.System.GetUtcNow().AddMinutes(-16));

        var upload = await urls.CreateUploadUrlAsync(NewName(), FifteenMinutes, TestContext.Current.CancellationToken);
        using var put = await PutAsync(upload.Url, RandomNumberGenerator.GetBytes(16));

        put.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_read_url_gets_no_other_blob()
    {
        var urls = await UrlsAsync("editorial", TimeProvider.System.GetUtcNow());
        var name = NewName();
        var other = NewName();

        using var putName = await PutAsync((await urls.CreateUploadUrlAsync(name, FiveMinutes, TestContext.Current.CancellationToken)).Url, RandomNumberGenerator.GetBytes(16));
        using var putOther = await PutAsync((await urls.CreateUploadUrlAsync(other, FiveMinutes, TestContext.Current.CancellationToken)).Url, RandomNumberGenerator.GetBytes(16));
        var read = await urls.CreateReadUrlAsync(name, FiveMinutes, TestContext.Current.CancellationToken);
        using var get = await Http.GetAsync(Elsewhere(read.Url, name, other), TestContext.Current.CancellationToken);

        putName.StatusCode.ShouldBe(HttpStatusCode.Created);
        putOther.StatusCode.ShouldBe(HttpStatusCode.Created);
        get.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_read_url_cannot_put()
    {
        var urls = await UrlsAsync("editorial", TimeProvider.System.GetUtcNow());

        var read = await urls.CreateReadUrlAsync(NewName(), FiveMinutes, TestContext.Current.CancellationToken);
        using var put = await PutAsync(read.Url, RandomNumberGenerator.GetBytes(16));

        put.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_expired_read_url_is_refused()
    {
        var now = TimeProvider.System.GetUtcNow();
        var urls = await UrlsAsync("editorial", now);
        var expired = await UrlsAsync("editorial", now.AddMinutes(-16));
        var name = NewName();

        using var put = await PutAsync((await urls.CreateUploadUrlAsync(name, FiveMinutes, TestContext.Current.CancellationToken)).Url, RandomNumberGenerator.GetBytes(16));
        var read = await expired.CreateReadUrlAsync(name, FifteenMinutes, TestContext.Current.CancellationToken);
        using var get = await Http.GetAsync(read.Url, TestContext.Current.CancellationToken);

        put.StatusCode.ShouldBe(HttpStatusCode.Created);
        get.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(BrowserMethods))]
    public async Task The_web_origin_may_put_and_get_from_the_browser(string method)
    {
        using var preflight = await PreflightAsync(WebOrigin, method);

        preflight.StatusCode.ShouldBe(HttpStatusCode.OK);
        preflight.Headers.TryGetValues("Access-Control-Allow-Origin", out var origins).ShouldBeTrue();
        origins.ShouldNotBeNull().ShouldBe([WebOrigin]);
    }

    [Fact]
    public async Task Another_origin_may_not()
    {
        using var preflight = await PreflightAsync(OtherOrigin, "PUT");

        preflight.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Stored_blobs_read_an_uploaded_blobs_content_type_length_and_block_type()
    {
        var urls = await UrlsAsync("editorial", TimeProvider.System.GetUtcNow());
        var blobs = await StoredBlobsAsync("editorial");
        var name = NewName();
        var content = RandomNumberGenerator.GetBytes(300);

        var upload = await urls.CreateUploadUrlAsync(name, FiveMinutes, TestContext.Current.CancellationToken);
        using var put = await PutAsync(upload.Url, content, "application/pdf");
        var stored = await blobs.FindAsync(name, TestContext.Current.CancellationToken);

        put.StatusCode.ShouldBe(HttpStatusCode.Created);
        stored.HasValue.ShouldBeTrue("an uploaded blob is found");
        stored.Value.ETag.ShouldNotBeNullOrWhiteSpace("the version of the blob found, which reading its start must match");
        stored.Value.ShouldBe(new StoredBlob(name, "application/pdf", 300, IsBlockBlob: true, ETag: stored.Value.ETag));
    }

    [Fact]
    public async Task Stored_blobs_find_nothing_for_a_name_never_uploaded()
    {
        var urls = await UrlsAsync("editorial", TimeProvider.System.GetUtcNow());
        var blobs = await StoredBlobsAsync("editorial");
        var uploaded = NewName();
        var never = NewName();

        using var put = await PutAsync((await urls.CreateUploadUrlAsync(uploaded, FiveMinutes, TestContext.Current.CancellationToken)).Url, RandomNumberGenerator.GetBytes(16));
        var found = await blobs.FindAsync(uploaded, TestContext.Current.CancellationToken);
        var missing = await blobs.FindAsync(never, TestContext.Current.CancellationToken);

        // The uploaded blob is found, so finding nothing for the other name says it is missing, not that the reader is blind.
        put.StatusCode.ShouldBe(HttpStatusCode.Created);
        found.HasValue.ShouldBeTrue("the blob uploaded beside it is found");
        missing.HasValue.ShouldBeFalse();
    }

    [Fact]
    public async Task Stored_blobs_throw_for_a_container_that_does_not_exist_rather_than_find_nothing()
    {
        // The editorial container's own connection string, naming a container the AppHost never created.
        var parts = (await ConnectionStringAsync("editorial")).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var elsewhere = string.Join(';', [.. parts.Where(part => !part.StartsWith("ContainerName=", StringComparison.OrdinalIgnoreCase)), $"ContainerName=missing-{Guid.CreateVersion7():N}"]);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:blobs"] = elsewhere })
            .Build();
        var blobs = new AzureStoredBlobs(configuration);

        var thrown = await Should.ThrowAsync<Azure.RequestFailedException>(() => blobs.FindAsync(NewName(), TestContext.Current.CancellationToken));

        thrown.ErrorCode.ShouldBe("ContainerNotFound", "a module whose container is missing is misconfigured; no upload is merely late");
    }

    [Fact]
    public async Task Stored_blobs_read_the_first_bytes_of_a_blob_or_all_of_a_shorter_one()
    {
        var urls = await UrlsAsync("editorial", TimeProvider.System.GetUtcNow());
        var blobs = await StoredBlobsAsync("editorial");
        var longer = NewName();
        var shorter = NewName();
        var longerContent = RandomNumberGenerator.GetBytes(300);
        var shorterContent = RandomNumberGenerator.GetBytes(5);

        using var putLonger = await PutAsync((await urls.CreateUploadUrlAsync(longer, FiveMinutes, TestContext.Current.CancellationToken)).Url, longerContent);
        using var putShorter = await PutAsync((await urls.CreateUploadUrlAsync(shorter, FiveMinutes, TestContext.Current.CancellationToken)).Url, shorterContent);
        var foundLonger = await blobs.FindAsync(longer, TestContext.Current.CancellationToken);
        var foundShorter = await blobs.FindAsync(shorter, TestContext.Current.CancellationToken);
        foundLonger.HasValue.ShouldBeTrue("the longer blob was uploaded");
        foundShorter.HasValue.ShouldBeTrue("the shorter blob was uploaded");
        var startOfLonger = await blobs.ReadStartAsync(foundLonger.Value, 8, TestContext.Current.CancellationToken);
        var startOfShorter = await blobs.ReadStartAsync(foundShorter.Value, 8, TestContext.Current.CancellationToken);

        putLonger.StatusCode.ShouldBe(HttpStatusCode.Created);
        putShorter.StatusCode.ShouldBe(HttpStatusCode.Created);
        startOfLonger.ToArray().ShouldBe(longerContent[..8], "a ranged read answers the first bytes asked for, and no more");
        startOfShorter.ToArray().ShouldBe(shorterContent, "a blob shorter than the range answers what it has");
    }

    [Fact]
    public async Task Stored_blobs_report_a_blob_a_server_side_copy_created_as_a_copy_and_an_uploaded_one_as_none()
    {
        var urls = await UrlsAsync("editorial", TimeProvider.System.GetUtcNow());
        var blobs = await StoredBlobsAsync("editorial");
        var container = await ContainerAsync("editorial");
        var source = NewName();
        var copy = NewName();

        using var put = await PutAsync((await urls.CreateUploadUrlAsync(source, FiveMinutes, TestContext.Current.CancellationToken)).Url, RandomNumberGenerator.GetBytes(300), "application/pdf");
        // Within one account the emulator copies from the source's own URL; a signed one it would fetch from inside its container.
        var copying = await container.GetBlobClient(copy.Value).StartCopyFromUriAsync(container.GetBlobClient(source.Value).Uri, cancellationToken: TestContext.Current.CancellationToken);
        await copying.WaitForCompletionAsync(TestContext.Current.CancellationToken);
        var properties = (await container.GetBlobClient(copy.Value).GetPropertiesAsync(cancellationToken: TestContext.Current.CancellationToken)).Value;
        var uploaded = await blobs.FindAsync(source, TestContext.Current.CancellationToken);
        var copied = await blobs.FindAsync(copy, TestContext.Current.CancellationToken);

        put.StatusCode.ShouldBe(HttpStatusCode.Created);
        properties.CopyStatus.ShouldBe(CopyStatus.Success, "the emulator ran the copy");
        uploaded.HasValue.ShouldBeTrue("the uploaded blob is found");
        uploaded.Value.IsCopy.ShouldBeFalse("a blob put directly is no copy");
        copied.HasValue.ShouldBeTrue("the copied blob is found");
        copied.Value.ShouldBe(new StoredBlob(copy, "application/pdf", 300, IsBlockBlob: true, IsCopy: true, ETag: copied.Value.ETag));
    }

    /// <summary>
    /// The modules whose containers the running AppHost created, read from its application model rather than listed, so a
    /// module added or removed with <c>fabric add-module</c> is covered without touching this file.
    /// </summary>
    private List<string> Modules() =>
        [.. aspire.App.Services.GetRequiredService<DistributedApplicationModel>().Resources
            .OfType<AzureBlobStorageContainerResource>()
            .Select(container => container.BlobContainerName)
            .Order(StringComparer.Ordinal)];

    private static BlobName NewName() => BlobName.From($"probes/{Guid.CreateVersion7()}.bin");

    /// <summary>The same signature, pointed at another blob.</summary>
    private static Uri Elsewhere(Uri url, BlobName name, BlobName other) =>
        new(url.AbsoluteUri.Replace(name.Value, other.Value, StringComparison.Ordinal));

    private static async Task<HttpResponseMessage> PutAsync(Uri url, byte[] content, string? contentType = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, url) { Content = new ByteArrayContent(content) };
        request.Headers.Add("x-ms-blob-type", "BlockBlob");
        if (contentType is not null)
        {
            request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        }

        return await Http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>A browser's CORS preflight for a blob upload or read, sent to the emulator's blob endpoint.</summary>
    private async Task<HttpResponseMessage> PreflightAsync(string origin, string method)
    {
        await WaitForHealthyAsync(Storage);
        var blobEndpoint = Part(await ConnectionStringAsync("editorial"), "BlobEndpoint").TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Options, new Uri($"{blobEndpoint}/editorial/{NewName().Value}"));
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", method);
        request.Headers.Add("Access-Control-Request-Headers", "x-ms-blob-type,content-type");
        return await Http.SendAsync(request, TestContext.Current.CancellationToken);
    }

    /// <summary>Urls for a module's container, signed at the given instant as the service would sign them.</summary>
    private async Task<AzureBlobUrls> UrlsAsync(string module, DateTimeOffset now)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:blobs"] = await ConnectionStringAsync(module) })
            .Build();
        return new AzureBlobUrls(configuration, new FakeTimeProvider(now));
    }

    /// <summary>The blobs of a module's container, read as the service reads them.</summary>
    private async Task<AzureStoredBlobs> StoredBlobsAsync(string module)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:blobs"] = await ConnectionStringAsync(module) })
            .Build();
        return new AzureStoredBlobs(configuration);
    }

    /// <summary>A module's container as an SDK client with the account's key, to do what no signed URL may: a server-side copy.</summary>
    private async Task<BlobContainerClient> ContainerAsync(string module)
    {
        var parts = (await ConnectionStringAsync(module)).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var account = string.Join(';', parts.Where(part => !part.StartsWith("ContainerName=", StringComparison.OrdinalIgnoreCase)));
        return new BlobServiceClient(account).GetBlobContainerClient(module);
    }

    private async Task<string> ConnectionStringAsync(string module)
    {
        var container = $"{module}-blobs";
        await WaitForHealthyAsync(container);
        return await aspire.App.GetConnectionStringAsync(container, TestContext.Current.CancellationToken)
            ?? throw new InvalidOperationException($"{container} has no connection string.");
    }

    private async Task WaitForHealthyAsync(string resource)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(ResourceTimeout);
        await aspire.App.ResourceNotifications.WaitForResourceHealthyAsync(resource, timeout.Token);
    }

    private static string Part(string connectionString, string key) =>
        connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Split('=', 2))
            .Where(pair => pair.Length == 2 && string.Equals(pair[0].Trim(), key, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair[1].Trim())
            .FirstOrDefault()
            ?? throw new InvalidOperationException($"The connection string has no {key}.");
}
