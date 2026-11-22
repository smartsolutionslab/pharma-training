using System.Globalization;
using System.Security.Cryptography;
using System.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage.Tests;

/// <summary>
/// The SAS URLs AzureBlobUrls signs, read back from the URL itself: one blob, one permission, an expiry no later than the
/// lifetime allows. The account key is made up for each run; no real account is ever named.
/// </summary>
public class AzureBlobUrlsTests
{
    private const string Account = "probeaccount";

    private const string Container = "editorial";

    private static readonly DateTimeOffset Noon = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private static readonly BlobUrlLifetime FiveMinutes = BlobUrlLifetime.From(TimeSpan.FromMinutes(5));

    [Fact]
    public async Task An_upload_url_creates_only_its_blob_until_the_lifetime_ends()
    {
        var urls = new AzureBlobUrls(Configuration(ConnectionString()), new FakeTimeProvider(Noon));

        var upload = await urls.CreateUploadUrlAsync(BlobName.From("courses/2026/handout.pdf"), FiveMinutes, TestContext.Current.CancellationToken);

        upload.Url.AbsolutePath.ShouldBe($"/{Account}/{Container}/courses/2026/handout.pdf");
        var query = HttpUtility.ParseQueryString(upload.Url.Query);
        query["sr"].ShouldBe("b");
        query["sp"].ShouldBe("c");
        query["st"].ShouldBeNull();
        query["sig"].ShouldNotBeNullOrWhiteSpace();
        Expiry(query).ShouldBe(Noon.AddMinutes(5));
        upload.ExpiresAt.ShouldBe(Noon.AddMinutes(5));
    }

    [Fact]
    public async Task A_read_url_reads_only_its_blob_until_the_lifetime_ends()
    {
        var urls = new AzureBlobUrls(Configuration(ConnectionString()), new FakeTimeProvider(Noon));

        var read = await urls.CreateReadUrlAsync(BlobName.From("courses/2026/handout.pdf"), FiveMinutes, TestContext.Current.CancellationToken);

        read.Url.AbsolutePath.ShouldBe($"/{Account}/{Container}/courses/2026/handout.pdf");
        var query = HttpUtility.ParseQueryString(read.Url.Query);
        query["sr"].ShouldBe("b");
        query["sp"].ShouldBe("r");
        query["st"].ShouldBeNull();
        query["sig"].ShouldNotBeNullOrWhiteSpace();
        Expiry(query).ShouldBe(Noon.AddMinutes(5));
        read.ExpiresAt.ShouldBe(Noon.AddMinutes(5));
    }

    [Fact]
    public async Task The_expiry_never_exceeds_the_lifetime()
    {
        var time = new FakeTimeProvider(Noon);
        time.SetUtcNow(Noon.AddMilliseconds(900));
        var urls = new AzureBlobUrls(Configuration(ConnectionString()), time);

        var upload = await urls.CreateUploadUrlAsync(BlobName.From("handout.pdf"), BlobUrlLifetime.From(TimeSpan.FromMinutes(15)), TestContext.Current.CancellationToken);

        Expiry(HttpUtility.ParseQueryString(upload.Url.Query)).ShouldBe(Noon.AddMinutes(15));
        upload.ExpiresAt.ShouldBe(Noon.AddMinutes(15));
    }

    [Fact]
    public async Task A_name_with_spaces_is_escaped_in_the_url()
    {
        var urls = new AzureBlobUrls(Configuration(ConnectionString()), new FakeTimeProvider(Noon));

        var read = await urls.CreateReadUrlAsync(BlobName.From("courses/hand out.pdf"), FiveMinutes, TestContext.Current.CancellationToken);

        read.Url.AbsoluteUri.ShouldNotContain(" ");
        read.Url.AbsolutePath.ShouldBe($"/{Account}/{Container}/courses/hand%20out.pdf");
    }

    [Fact]
    public async Task A_missing_connection_string_fails_when_a_url_is_first_created_and_names_it()
    {
        var urls = new AzureBlobUrls(new ConfigurationBuilder().Build(), new FakeTimeProvider(Noon));

        var failure = await Should.ThrowAsync<InvalidOperationException>(() =>
            urls.CreateUploadUrlAsync(BlobName.From("handout.pdf"), FiveMinutes, TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("ConnectionStrings:blobs");
    }

    [Fact]
    public async Task A_connection_string_without_a_container_fails_naming_ContainerName()
    {
        var withoutContainer = $"DefaultEndpointsProtocol=http;AccountName={Account};AccountKey={Key()};BlobEndpoint=http://127.0.0.1:10000/{Account}";
        var urls = new AzureBlobUrls(Configuration(withoutContainer), new FakeTimeProvider(Noon));

        var failure = await Should.ThrowAsync<InvalidOperationException>(() =>
            urls.CreateReadUrlAsync(BlobName.From("handout.pdf"), FiveMinutes, TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("ContainerName");
    }

    [Fact]
    public async Task Without_an_account_key_or_a_managed_identity_it_fails_when_a_url_is_first_created_naming_both()
    {
        // As published: the account's endpoint and the container, no key; and no AZURE_CLIENT_ID to sign keyless with.
        var withoutKey = $"Endpoint=https://{Account}.blob.core.windows.net/;ContainerName={Container}";
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FakeTimeProvider(Noon));
        services.AddModuleBlobStorage(Configuration(withoutKey));
        using var provider = services.BuildServiceProvider();
        var urls = provider.GetRequiredService<IBlobUrls>();

        var failure = await Should.ThrowAsync<InvalidOperationException>(() =>
            urls.CreateUploadUrlAsync(BlobName.From("handout.pdf"), FiveMinutes, TestContext.Current.CancellationToken));

        failure.Message.ShouldContain("AZURE_CLIENT_ID");
        failure.Message.ShouldContain("AccountKey");
        failure.Message.ShouldNotContain("separate decision", Case.Insensitive);
    }

    /// <summary>An account key made up for this run, as the emulator's connection string carries one.</summary>
    private static string Key() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    private static string ConnectionString() =>
        $"DefaultEndpointsProtocol=http;AccountName={Account};AccountKey={Key()};BlobEndpoint=http://127.0.0.1:10000/{Account};ContainerName={Container}";

    private static IConfiguration Configuration(string connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:blobs"] = connectionString })
            .Build();

    private static DateTimeOffset Expiry(System.Collections.Specialized.NameValueCollection query) =>
        DateTimeOffset.Parse(query["se"] ?? throw new InvalidOperationException("The URL has no expiry (se)."), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
}
