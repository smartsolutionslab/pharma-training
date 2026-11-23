using System.Collections.Specialized;
using System.Globalization;
using System.Security.Cryptography;
using System.Web;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Time.Testing;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage.Tests;

/// <summary>
/// Published, the <c>blobs</c> connection string has the account's endpoint and the container but no key (shared-key
/// access is off, docs/adr/0002), and AzureBlobUrls signs with a user delegation key it gets from the storage service
/// under the service's managed identity. The key source is that boundary, substituted here; no real account is named.
/// </summary>
public class AzureBlobUrlsUserDelegationTests
{
    private const string Account = "probeaccount";

    private const string Container = "editorial";

    private static readonly DateTimeOffset Noon = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);

    private static readonly BlobUrlLifetime FiveMinutes = BlobUrlLifetime.From(TimeSpan.FromMinutes(5));

    private static readonly BlobReadHeaders InlinePdf = BlobReadHeaders.Inline("application/pdf");

    private static readonly Uri BlobService = new($"https://{Account}.blob.core.windows.net/");

    [Fact]
    public async Task Without_an_account_key_it_signs_an_upload_url_with_a_user_delegation_key()
    {
        var key = Key(Noon.AddHours(1));
        var keys = KeySource(key);
        var urls = new AzureBlobUrls(Configuration(Keyless()), new FakeTimeProvider(Noon), keys);

        var upload = await urls.CreateUploadUrlAsync(BlobName.From("courses/2026/handout.pdf"), FiveMinutes, TestContext.Current.CancellationToken);

        upload.Url.Host.ShouldBe($"{Account}.blob.core.windows.net");
        upload.Url.AbsolutePath.ShouldBe($"/{Container}/courses/2026/handout.pdf");
        var query = HttpUtility.ParseQueryString(upload.Url.Query);
        query["sr"].ShouldBe("b");
        query["sp"].ShouldBe("c");
        query["st"].ShouldBeNull();
        query["sig"].ShouldNotBeNullOrWhiteSpace();
        query["skoid"].ShouldBe(key.SignedObjectId);
        query["sktid"].ShouldBe(key.SignedTenantId);
        Expiry(query).ShouldBe(Noon.AddMinutes(5));
        upload.ExpiresAt.ShouldBe(Noon.AddMinutes(5));
        await keys.Received(1).GetKeyAsync(BlobService, Arg.Is<DateTimeOffset>(expiresOn => expiresOn >= Noon.AddMinutes(5)), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Without_an_account_key_it_signs_a_read_url_with_a_user_delegation_key()
    {
        var key = Key(Noon.AddHours(1));
        var urls = new AzureBlobUrls(Configuration(Keyless()), new FakeTimeProvider(Noon), KeySource(key));

        var read = await urls.CreateReadUrlAsync(BlobName.From("courses/2026/handout.pdf"), FiveMinutes, InlinePdf, TestContext.Current.CancellationToken);

        read.Url.AbsolutePath.ShouldBe($"/{Container}/courses/2026/handout.pdf");
        var query = HttpUtility.ParseQueryString(read.Url.Query);
        query["sr"].ShouldBe("b");
        query["sp"].ShouldBe("r");
        query["st"].ShouldBeNull();
        query["sig"].ShouldNotBeNullOrWhiteSpace();
        query["skoid"].ShouldBe(key.SignedObjectId);
        Expiry(query).ShouldBe(Noon.AddMinutes(5));
        read.ExpiresAt.ShouldBe(Noon.AddMinutes(5));
    }

    [Fact]
    public async Task A_user_delegation_read_url_carries_the_same_overrides()
    {
        var key = Key(Noon.AddHours(1));
        var urls = new AzureBlobUrls(Configuration(Keyless()), new FakeTimeProvider(Noon), KeySource(key));

        var inline = await urls.CreateReadUrlAsync(BlobName.From("courses/2026/praxis.mp4"), FiveMinutes, BlobReadHeaders.Inline("video/mp4"), TestContext.Current.CancellationToken);
        var attachment = await urls.CreateReadUrlAsync(BlobName.From("exports/report.csv"), FiveMinutes, BlobReadHeaders.Attachment("text/csv"), TestContext.Current.CancellationToken);

        var inlineQuery = HttpUtility.ParseQueryString(inline.Url.Query);
        inlineQuery["skoid"].ShouldBe(key.SignedObjectId);
        inlineQuery["sp"].ShouldBe("r");
        inlineQuery["rsct"].ShouldBe("video/mp4");
        inlineQuery["rscd"].ShouldBe("inline");
        var attachmentQuery = HttpUtility.ParseQueryString(attachment.Url.Query);
        attachmentQuery["rsct"].ShouldBe("text/csv");
        attachmentQuery["rscd"].ShouldBe("attachment");
    }

    [Fact]
    public async Task Without_an_account_key_both_urls_of_an_https_endpoint_work_over_https_alone()
    {
        var urls = new AzureBlobUrls(Configuration(Keyless()), new FakeTimeProvider(Noon), KeySource(Key(Noon.AddHours(1))));

        var upload = await urls.CreateUploadUrlAsync(BlobName.From("handout.pdf"), FiveMinutes, TestContext.Current.CancellationToken);
        var read = await urls.CreateReadUrlAsync(BlobName.From("handout.pdf"), FiveMinutes, InlinePdf, TestContext.Current.CancellationToken);

        HttpUtility.ParseQueryString(upload.Url.Query)["spr"].ShouldBe("https", "a signature for an https endpoint must not work over plain http");
        HttpUtility.ParseQueryString(read.Url.Query)["spr"].ShouldBe("https", "a signature for an https endpoint must not work over plain http");
    }

    [Fact]
    public async Task Without_an_account_key_the_expiry_is_rounded_down_to_the_second()
    {
        var time = new FakeTimeProvider(Noon);
        time.SetUtcNow(Noon.AddMilliseconds(900));
        var urls = new AzureBlobUrls(Configuration(Keyless()), time, KeySource(Key(Noon.AddHours(1))));

        var upload = await urls.CreateUploadUrlAsync(BlobName.From("handout.pdf"), BlobUrlLifetime.From(TimeSpan.FromMinutes(15)), TestContext.Current.CancellationToken);

        Expiry(HttpUtility.ParseQueryString(upload.Url.Query)).ShouldBe(Noon.AddMinutes(15));
        upload.ExpiresAt.ShouldBe(Noon.AddMinutes(15));
    }

    [Fact]
    public async Task Without_an_account_key_it_reuses_the_key_until_it_nearly_expires()
    {
        var time = new FakeTimeProvider(Noon);
        var first = Key(Noon.AddHours(1));
        var second = Key(Noon.AddHours(2));
        var keys = KeySource(first, second);
        var urls = new AzureBlobUrls(Configuration(Keyless()), time, keys);
        var name = BlobName.From("handout.pdf");

        var atNoon = await urls.CreateReadUrlAsync(name, FiveMinutes, InlinePdf, TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromMinutes(30));
        var halfAnHourLater = await urls.CreateReadUrlAsync(name, FiveMinutes, InlinePdf, TestContext.Current.CancellationToken);

        await keys.Received(1).GetKeyAsync(Arg.Any<Uri>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        HttpUtility.ParseQueryString(atNoon.Url.Query)["skoid"].ShouldBe(first.SignedObjectId);
        HttpUtility.ParseQueryString(halfAnHourLater.Url.Query)["skoid"].ShouldBe(first.SignedObjectId);

        // At 12:56 a five-minute URL would outlive the first key (13:00), so a new one is fetched before signing it.
        time.Advance(TimeSpan.FromMinutes(26));
        var nearExpiry = await urls.CreateReadUrlAsync(name, FiveMinutes, InlinePdf, TestContext.Current.CancellationToken);

        await keys.Received(2).GetKeyAsync(Arg.Any<Uri>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        var query = HttpUtility.ParseQueryString(nearExpiry.Url.Query);
        query["skoid"].ShouldBe(second.SignedObjectId);
        Expiry(query).ShouldBe(Noon.AddMinutes(61));
    }

    [Fact]
    public async Task With_an_account_key_it_never_asks_for_a_user_delegation_key()
    {
        var keys = Substitute.For<IUserDelegationKeySource>();
        var urls = new AzureBlobUrls(Configuration(WithAccountKey()), new FakeTimeProvider(Noon), keys);

        var upload = await urls.CreateUploadUrlAsync(BlobName.From("handout.pdf"), FiveMinutes, TestContext.Current.CancellationToken);

        HttpUtility.ParseQueryString(upload.Url.Query)["skoid"].ShouldBeNull();
        await keys.DidNotReceiveWithAnyArgs().GetKeyAsync(default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Creating_it_without_an_account_key_asks_for_no_key_until_a_url_is_signed()
    {
        var keys = Substitute.For<IUserDelegationKeySource>();

        _ = new AzureBlobUrls(Configuration(Keyless()), new FakeTimeProvider(Noon), keys);

        await keys.DidNotReceiveWithAnyArgs().GetKeyAsync(default!, default, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Without_an_account_key_the_url_is_valid_over_https_alone()
    {
        var urls = new AzureBlobUrls(Configuration(Keyless()), new FakeTimeProvider(Noon), KeySource(Key(Noon.AddHours(1))));

        var upload = await urls.CreateUploadUrlAsync(BlobName.From("handout.pdf"), FiveMinutes, TestContext.Current.CancellationToken);
        var read = await urls.CreateReadUrlAsync(BlobName.From("handout.pdf"), FiveMinutes, BlobReadHeaders.Inline("application/pdf"), TestContext.Current.CancellationToken);

        HttpUtility.ParseQueryString(upload.Url.Query)["spr"].ShouldBe("https", $"The upload URL should be signed for https alone: {upload.Url}");
        HttpUtility.ParseQueryString(read.Url.Query)["spr"].ShouldBe("https", $"The read URL should be signed for https alone: {read.Url}");
    }

    [Fact]
    public async Task Without_an_account_key_an_endpoint_that_is_not_an_account_blob_host_fails_when_the_first_url_is_signed_naming_it()
    {
        const string endpoint = "https://files.contoso.example/";
        var urls = new AzureBlobUrls(Configuration($"Endpoint={endpoint};ContainerName={Container}"), new FakeTimeProvider(Noon), KeySource(Key(Noon.AddHours(1))));

        var failure = await Should.ThrowAsync<InvalidOperationException>(() =>
            urls.CreateUploadUrlAsync(BlobName.From("handout.pdf"), FiveMinutes, TestContext.Current.CancellationToken));

        failure.Message.ShouldContain(endpoint.TrimEnd('/'));
    }

    [Fact]
    public async Task Without_an_account_key_urls_signed_at_once_ask_for_one_key()
    {
        var pending = new TaskCompletionSource<UserDelegationKey>(TaskCreationOptions.RunContinuationsAsynchronously);
        var keys = Substitute.For<IUserDelegationKeySource>();
        keys.GetKeyAsync(Arg.Any<Uri>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
        var urls = new AzureBlobUrls(Configuration(Keyless()), new FakeTimeProvider(Noon), keys);
        var key = Key(Noon.AddHours(1));

        var signing = Enumerable.Range(0, 8)
            .Select(index => urls.CreateUploadUrlAsync(BlobName.From($"handout-{index}.pdf"), FiveMinutes, TestContext.Current.CancellationToken))
            .ToList();
        pending.SetResult(key);
        var signed = await Task.WhenAll(signing);

        await keys.Received(1).GetKeyAsync(Arg.Any<Uri>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        signed.ShouldAllBe(url => HttpUtility.ParseQueryString(url.Url.Query)["skoid"] == key.SignedObjectId);
    }

    [Fact]
    public async Task Without_an_account_key_a_shorter_lived_key_that_arrives_later_never_replaces_a_longer_lived_one()
    {
        var time = new FakeTimeProvider(Noon);
        var shortLived = new TaskCompletionSource<UserDelegationKey>(TaskCreationOptions.RunContinuationsAsynchronously);
        var longLived = Key(Noon.AddHours(2));
        var keys = Substitute.For<IUserDelegationKeySource>();
        keys.GetKeyAsync(Arg.Any<Uri>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(shortLived.Task, Task.FromResult(longLived), Task.FromResult(Key(Noon.AddHours(3))));
        var urls = new AzureBlobUrls(Configuration(Keyless()), time, keys);
        var name = BlobName.From("handout.pdf");

        // At noon a key valid until 13:00 is asked for and takes its time.
        var atNoon = urls.CreateReadUrlAsync(name, FiveMinutes, BlobReadHeaders.Inline("application/pdf"), TestContext.Current.CancellationToken);
        // At 12:58 a five-minute URL (13:03) would outlive it, so another key, valid until 14:00, is asked for and arrives.
        time.Advance(TimeSpan.FromMinutes(58));
        var later = await urls.CreateReadUrlAsync(name, FiveMinutes, BlobReadHeaders.Inline("application/pdf"), TestContext.Current.CancellationToken);
        // Then the first key, valid until 13:00, arrives; the one valid until 14:00 is kept.
        shortLived.SetResult(Key(Noon.AddHours(1)));
        await atNoon;
        var afterBoth = await urls.CreateReadUrlAsync(name, FiveMinutes, BlobReadHeaders.Inline("application/pdf"), TestContext.Current.CancellationToken);

        HttpUtility.ParseQueryString(later.Url.Query)["skoid"].ShouldBe(longLived.SignedObjectId);
        HttpUtility.ParseQueryString(afterBoth.Url.Query)["skoid"].ShouldBe(longLived.SignedObjectId, "A key that expires at 13:00 replaced the one kept until 14:00.");
        await keys.Received(2).GetKeyAsync(Arg.Any<Uri>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    /// <summary>The connection string as the AppHost publishes it: the account's endpoint and the container, no key.</summary>
    private static string Keyless() => $"Endpoint=https://{Account}.blob.core.windows.net/;ContainerName={Container}";

    private static string WithAccountKey() =>
        $"DefaultEndpointsProtocol=http;AccountName={Account};AccountKey={Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))};BlobEndpoint=http://127.0.0.1:10000/{Account};ContainerName={Container}";

    private static IUserDelegationKeySource KeySource(UserDelegationKey key, params UserDelegationKey[] then)
    {
        var keys = Substitute.For<IUserDelegationKeySource>();
        keys.GetKeyAsync(Arg.Any<Uri>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(key), [.. then.Select(Task.FromResult)]);
        return keys;
    }

    /// <summary>A user delegation key made up for this run, valid from an hour before noon until it expires.</summary>
    private static UserDelegationKey Key(DateTimeOffset expiresOn) =>
        // Positional: named arguments are ambiguous between two seven-parameter overloads. Object id, tenant id, starts on,
        // expires on, service, version, the key's value.
        BlobsModelFactory.UserDelegationKey(
            Guid.CreateVersion7().ToString(),
            Guid.CreateVersion7().ToString(),
            Noon.AddHours(-1),
            expiresOn,
            "b",
            "2025-01-05",
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));

    private static IConfiguration Configuration(string connectionString) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:blobs"] = connectionString })
            .Build();

    private static DateTimeOffset Expiry(NameValueCollection query) =>
        DateTimeOffset.Parse(query["se"] ?? throw new InvalidOperationException("The URL has no expiry (se)."), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);
}
