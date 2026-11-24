using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Aspire.Hosting.Testing;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.EditorialWorkflow;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningDatabase;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Republishing also releases a published version's files again (#264): <c>ChapterFilesReleasedV1</c> goes out once
/// more, with read URLs Editorial signs when the republish runs, so a Learning that never copied a file (deployed late,
/// or its first release expired before it copied) gets it, and one that holds it already keeps its one copy.
/// </summary>
[Collection(EditorialDrafts.Name)]
public class EditorialRepublishFilesTests(AspireFixture aspire)
{
    private const string ChapterFilesReleasedExchange = "SmartSolutionsLab.PharmaTraining.Contracts.Editorial.ChapterFilesReleasedV1";
    private const string DeadLettersById = "select count(*) from wolverine.wolverine_dead_letters where id = @id";

    private static readonly Uri Republish = new("/editorial/course-versions/republish", UriKind.Relative);
    private static readonly TimeSpan ResourceTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan UrlLifetime = TimeSpan.FromMinutes(15);

    /// <summary>Reads a signed URL straight from the storage emulator, as Learning's copy and a learner's browser do.</summary>
    private static readonly HttpClient Storage = new();

    /// <summary>
    /// The queue is bound after the version was published, so only the republish can bring its release. The URL expires
    /// fifteen minutes after it was signed, rounded down to the second, so one signed at the republish expires no earlier
    /// than fifteen minutes after the request went out, less that second.
    /// </summary>
    [Fact]
    public async Task A_queue_bound_after_a_version_with_a_pdf_was_published_receives_its_file_release_with_a_url_signed_at_the_republish()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var bytes = PdfBytes();
        using var editor = await aspire.CreateHostClientAsync("editor");
        using var publisher = await aspire.CreateHostClientAsync(Publisher);
        var (versionId, chapterId) = await PublishedWithPdfChapterAsync(editor, publisher, bytes, cancellationToken);

        await using var connection = await Broker.ConnectAsync(aspire, cancellationToken);
        await Broker.WaitForExchangeAsync(connection, ChapterFilesReleasedExchange, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var queue = await Broker.BindQueueAsync(channel, ChapterFilesReleasedExchange, cancellationToken);
        var republishedFrom = TimeProvider.System.GetUtcNow();

        var counts = await RepublishAsync(editor, cancellationToken);

        counts.FileReleases.ShouldBeGreaterThanOrEqualTo(1);
        counts.FileReleases.ShouldBeLessThanOrEqualTo(counts.CourseVersions);
        var released = await Broker.ReceiveAsync<JsonElement>(channel, queue, received => VersionOf(received) == versionId, cancellationToken);
        var file = released.GetProperty("files").EnumerateArray().ShouldHaveSingleItem();
        file.GetProperty("chapterId").GetGuid().ShouldBe(chapterId);
        file.GetProperty("kind").GetString().ShouldBe("Pdf");
        var expiresAt = file.GetProperty("expiresAt").GetDateTimeOffset();
        expiresAt.ShouldBeGreaterThanOrEqualTo(republishedFrom.Add(UrlLifetime).AddSeconds(-1));
        expiresAt.ShouldBeLessThanOrEqualTo(TimeProvider.System.GetUtcNow().Add(UrlLifetime));
        var readUrl = new Uri(file.GetProperty("readUrl").GetString().ShouldNotBeNull());
        readUrl.AbsolutePath.ShouldContain("/editorial/");
        using var read = await Storage.GetAsync(readUrl, cancellationToken);
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync(cancellationToken));
        read.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("application/pdf");
        (await read.Content.ReadAsByteArrayAsync(cancellationToken)).ShouldBe(bytes);
    }

    /// <summary>
    /// Learning copied the file when the version was published; the test then takes the copy away, which leaves Learning
    /// as one that never copied it, and the media answers 409 again. The republished release brings it back from a URL
    /// signed at the republish; a second republish finds it there and copies nothing. Both releases are read off a queue
    /// of the test's own under the message-id Learning's inbox stores, so the test waits until Learning handled exactly them.
    /// </summary>
    [Fact]
    public async Task Learning_copies_a_file_it_lacks_from_the_republished_release_and_keeps_one_copy_when_it_is_republished_again()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var bytes = PdfBytes();
        using var editor = await aspire.CreateHostClientAsync("editor");
        using var learner = await aspire.CreateHostClientAsync("employee-a");
        await MemberAsync(aspire, SubjectOf(learner), cancellationToken);
        using var publisher = await aspire.CreateHostClientAsync(Publisher);
        var (versionId, chapterId) = await PublishedWithPdfChapterAsync(editor, publisher, bytes, cancellationToken);
        (await WaitUntilAsync(() => StartAsync(learner, versionId, cancellationToken), HttpStatusCode.Created, $"POST {Sessions} for version {versionId}", cancellationToken)).Dispose();
        (await WaitUntilAsync(() => learner.GetAsync(Media(chapterId), cancellationToken), HttpStatusCode.OK, $"GET {Media(chapterId)}", cancellationToken)).Dispose();

        var learning = await LearningContainerAsync(cancellationToken);
        var copy = $"course-versions/{versionId:D}/chapters/{chapterId:D}.pdf";
        (await learning.GetBlobClient(copy).DeleteAsync(cancellationToken: cancellationToken)).IsError.ShouldBeFalse();
        using (var lacking = await learner.GetAsync(Media(chapterId), cancellationToken))
        {
            lacking.StatusCode.ShouldBe(HttpStatusCode.Conflict, await lacking.Content.ReadAsStringAsync(cancellationToken));
            (await CodeOfAsync(lacking, cancellationToken)).ShouldBe("learning.media-not-ready");
        }

        await using var connection = await Broker.ConnectAsync(aspire, cancellationToken);
        await Broker.WaitForExchangeAsync(connection, ChapterFilesReleasedExchange, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var queue = await Broker.BindQueueAsync(channel, ChapterFilesReleasedExchange, cancellationToken);

        await RepublishAsync(editor, cancellationToken);
        var (first, _) = await Broker.ReceiveWithMessageIdAsync(channel, queue, received => VersionOf(received) == versionId, cancellationToken);
        await WaitUntilHandledAsync(aspire, first, cancellationToken);

        MediaBody answer;
        using (var media = await WaitUntilAsync(() => learner.GetAsync(Media(chapterId), cancellationToken), HttpStatusCode.OK, $"GET {Media(chapterId)} after the republish", cancellationToken))
        {
            answer = (await media.Content.ReadFromJsonAsync<MediaBody>(cancellationToken)).ShouldNotBeNull();
        }

        answer.Url.AbsolutePath.ShouldContain("/learning/course-versions/");
        using (var read = await Storage.GetAsync(answer.Url, cancellationToken))
        {
            read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync(cancellationToken));
            (await read.Content.ReadAsByteArrayAsync(cancellationToken)).ShouldBe(bytes);
        }

        await RepublishAsync(editor, cancellationToken);
        var (second, _) = await Broker.ReceiveWithMessageIdAsync(channel, queue, received => VersionOf(received) == versionId, cancellationToken);
        second.ShouldNotBe(first);
        await WaitUntilHandledAsync(aspire, second, cancellationToken);

        List<string> copies = [];
        await foreach (var blob in learning.GetBlobsAsync(BlobTraits.None, BlobStates.None, $"course-versions/{versionId:D}/", cancellationToken))
        {
            copies.Add(blob.Name);
        }

        copies.ShouldBe([copy]);
        (await CountAsync(aspire, DeadLettersById, first, cancellationToken)).ShouldBe(0);
        (await CountAsync(aspire, DeadLettersById, second, cancellationToken)).ShouldBe(0);
    }

    private static byte[] PdfBytes() =>
        Encoding.ASCII.GetBytes($"%PDF-1.4\n% {Guid.CreateVersion7()}\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n");

    private static Uri Media(Guid chapterId) => new($"/learning/media/{chapterId}", UriKind.Relative);

    private static Guid VersionOf(JsonElement received) => received.GetProperty("courseVersionId").GetGuid();

    private static async Task<RepublishedCounts> RepublishAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsync(Republish, null, cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"POST {Republish} answered {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(cancellationToken)}");
        return (await response.Content.ReadFromJsonAsync<RepublishedCounts>(cancellationToken)).ShouldNotBeNull();
    }

    /// <summary>Learning's container as an SDK client with the emulator's account key, to take a copy away as no signed URL may.</summary>
    private async Task<BlobContainerClient> LearningContainerAsync(CancellationToken cancellationToken)
    {
        using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeout.CancelAfter(ResourceTimeout);
            await aspire.App.ResourceNotifications.WaitForResourceHealthyAsync("learning-blobs", timeout.Token);
        }

        var connectionString = await aspire.App.GetConnectionStringAsync("learning-blobs", cancellationToken)
            ?? throw new InvalidOperationException("learning-blobs has no connection string.");
        var parts = connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var account = string.Join(';', parts.Where(part => !part.StartsWith("ContainerName=", StringComparison.OrdinalIgnoreCase)));
        return new BlobServiceClient(account).GetBlobContainerClient("learning");
    }

    /// <summary>Sends the request until it answers the wanted status, for at most a minute; the response is the caller's to dispose.</summary>
    private static async Task<HttpResponseMessage> WaitUntilAsync(Func<Task<HttpResponseMessage>> send, HttpStatusCode wanted, string request, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            var response = await send();
            if (response.StatusCode == wanted)
            {
                return response;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var status = response.StatusCode;
            response.Dispose();
            if (TimeProvider.System.GetElapsedTime(started) >= WaitTimeout)
            {
                throw new TimeoutException($"{request} still answered {(int)status} {status} ({body}) after {WaitTimeout.TotalSeconds} s, not {(int)wanted}.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    /// <summary>The answer as the test reads it; the module's DTO is not referenced.</summary>
    private sealed record RepublishedCounts(int CourseVersions, int TestDefinitions, int FileReleases, int Retirements);

    private sealed record MediaBody(Uri Url, DateTimeOffset ExpiresAt, string ContentType, string Kind);
}
