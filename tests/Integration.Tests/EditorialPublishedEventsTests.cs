using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Web;
using RabbitMQ.Client;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.EditorialWorkflow;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Publishing a course version through the edge announces it on the broker (story E6): once as the course with its
/// chapters and without its questions, for every module, and once as its test with the solutions, for Assessment. A
/// refused second publish announces nothing more, and Catalog accepts the real announcement as it accepts a hand-built one.
/// </summary>
[Collection(EditorialDrafts.Name)]
public class EditorialPublishedEventsTests(AspireFixture aspire)
{
    private const string CourseVersionPublishedExchange = "SmartSolutionsLab.PharmaTraining.Contracts.Editorial.CourseVersionPublishedV1";
    private const string TestDefinitionPublishedExchange = "SmartSolutionsLab.PharmaTraining.Contracts.Editorial.TestDefinitionPublishedV1";
    private const string ChapterFilesReleasedExchange = "SmartSolutionsLab.PharmaTraining.Contracts.Editorial.ChapterFilesReleasedV1";

    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);
    private static readonly string[] SolutionProperties = ["questions", "options", "isCorrect"];
    private static readonly byte[] SmallPdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n");

    /// <summary>
    /// Counts on queues of the test's own, up to a marker version published after the refused attempt. Editorial sends
    /// the events of one type in the order it commits them, so the marker's announcement comes after any the refused
    /// attempt would have sent.
    /// </summary>
    [Fact]
    public async Task Publishing_a_version_sends_one_course_version_and_one_test_definition_and_publishing_it_again_sends_nothing_more()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        await using var connection = await Broker.ConnectAsync(aspire, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        var listener = await ListenAsync(client, connection, channel, cancellationToken);

        var published = await PublishedAsync(client, cancellationToken);
        using (var again = await StepAsync(client, published.Id, "publish", cancellationToken))
        {
            again.StatusCode.ShouldBe(HttpStatusCode.Conflict);
            (await CodeOfAsync(again, cancellationToken)).ShouldBe("editorial.not-in-review");
        }

        var marker = await PublishedAsync(client, cancellationToken);
        var courses = await Broker.ReceiveUntilAsync<JsonElement>(channel, listener.CourseQueue, received => VersionOf(received) == marker.Id, cancellationToken);
        var tests = await Broker.ReceiveUntilAsync<JsonElement>(channel, listener.TestQueue, received => VersionOf(received) == marker.Id, cancellationToken);

        var course = courses.Where(received => VersionOf(received) == published.Id).ShouldHaveSingleItem();
        course.GetProperty("courseId").GetGuid().ShouldBe(published.CourseId);
        course.GetProperty("versionNumber").GetInt32().ShouldBe(1);
        course.GetProperty("validityMonths").GetInt32().ShouldBe(12);
        var chapter = course.GetProperty("chapters").EnumerateArray().ShouldHaveSingleItem();
        chapter.GetProperty("chapterId").GetGuid().ShouldBe(published.Chapter);
        chapter.GetProperty("position").GetInt32().ShouldBe(1);
        chapter.GetProperty("kind").GetString().ShouldBe("Text");
        chapter.GetProperty("videoDurationSeconds").ValueKind.ShouldBe(JsonValueKind.Null);
        PropertyNamesIn(course).Where(name => SolutionProperties.Contains(name, StringComparer.OrdinalIgnoreCase)).ShouldBeEmpty(course.GetRawText());

        var test = tests.Where(received => VersionOf(received) == published.Id).ShouldHaveSingleItem();
        test.GetProperty("courseId").GetGuid().ShouldBe(published.CourseId);
        test.GetProperty("passingScorePercent").GetInt32().ShouldBe(80);
        test.GetProperty("maxAttempts").GetInt32().ShouldBe(3);
        var question = test.GetProperty("questions").EnumerateArray().ShouldHaveSingleItem();
        question.GetProperty("questionId").GetGuid().ShouldBe(published.Question);
        question.GetProperty("chapterId").GetGuid().ShouldBe(published.Chapter);
        question.GetProperty("options").EnumerateArray().Count(option => option.GetProperty("isCorrect").GetBoolean()).ShouldBe(1);
    }

    /// <summary>Editorial's real announcement passes Catalog's consumer, which only hand-built ones had been through.</summary>
    [Fact]
    public async Task A_published_version_reaches_the_catalogue_with_its_chapters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");

        var published = await PublishedAsync(client, cancellationToken);

        var version = await WaitUntilReadableAsync(client, published.Id, cancellationToken);
        version.CourseId.ShouldBe(published.CourseId);
        version.VersionNumber.ShouldBe(1);
        version.ValidityMonths.ShouldBe(12);
        var chapter = version.Chapters.ShouldHaveSingleItem();
        chapter.ChapterId.ShouldBe(published.Chapter);
        chapter.Kind.ShouldBe("Text");
    }

    /// <summary>
    /// #41: a version with a PDF chapter also releases the chapter's file, with a read URL Editorial signed on its own
    /// container for fifteen minutes, to be shown inline as a PDF. A version of the test's own is published first, so
    /// Wolverine has declared the exchange, which no queue was bound to before.
    /// </summary>
    [Fact]
    public async Task Publishing_a_version_with_a_pdf_chapter_puts_its_released_files_on_the_broker()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        await using var connection = await Broker.ConnectAsync(aspire, cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
        await PublishedWithPdfChapterAsync(client, SmallPdf, cancellationToken);
        await Broker.WaitForExchangeAsync(connection, ChapterFilesReleasedExchange, cancellationToken);
        var queue = await Broker.BindQueueAsync(channel, ChapterFilesReleasedExchange, cancellationToken);

        var signedAfter = TimeProvider.System.GetUtcNow();
        var (versionId, chapterId) = await PublishedWithPdfChapterAsync(client, SmallPdf, cancellationToken);

        var released = await Broker.ReceiveAsync<JsonElement>(channel, queue, received => VersionOf(received) == versionId, cancellationToken);
        var file = released.GetProperty("files").EnumerateArray().ShouldHaveSingleItem();
        file.GetProperty("chapterId").GetGuid().ShouldBe(chapterId);
        file.GetProperty("kind").GetString().ShouldBe("Pdf");
        var readUrl = new Uri(file.GetProperty("readUrl").GetString().ShouldNotBeNull());
        readUrl.AbsolutePath.ShouldContain("/editorial/course-versions/");
        var query = HttpUtility.ParseQueryString(readUrl.Query);
        query["sp"].ShouldBe("r");
        query["rsct"].ShouldBe("application/pdf");
        query["rscd"].ShouldBe("inline");
        query["sig"].ShouldNotBeNullOrWhiteSpace();
        var expiresAt = file.GetProperty("expiresAt").GetDateTimeOffset();
        expiresAt.ShouldBeGreaterThan(signedAfter);
        expiresAt.ShouldBeLessThanOrEqualTo(TimeProvider.System.GetUtcNow().AddMinutes(15));
    }

    /// <summary>
    /// Binds a queue to each of the two exchanges. A version of the listener's own is published first, so Editorial has
    /// sent a test definition and Wolverine has declared its exchange, which no consumer declares yet.
    /// </summary>
    private static async Task<PublishedListener> ListenAsync(HttpClient client, IConnection connection, IChannel channel, CancellationToken cancellationToken)
    {
        await PublishedAsync(client, cancellationToken);
        await Broker.WaitForExchangeAsync(connection, CourseVersionPublishedExchange, cancellationToken);
        await Broker.WaitForExchangeAsync(connection, TestDefinitionPublishedExchange, cancellationToken);
        return new PublishedListener(
            await Broker.BindQueueAsync(channel, CourseVersionPublishedExchange, cancellationToken),
            await Broker.BindQueueAsync(channel, TestDefinitionPublishedExchange, cancellationToken));
    }

    private static Guid VersionOf(JsonElement received) => received.GetProperty("courseVersionId").GetGuid();

    private static Uri CatalogVersionUri(Guid id) => new($"/catalog/course-versions/{id}", UriKind.Relative);

    /// <summary>The version is in the catalogue once Catalog answers it.</summary>
    private static async Task<CatalogVersion> WaitUntilReadableAsync(HttpClient client, Guid courseVersionId, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            using var response = await client.GetAsync(CatalogVersionUri(courseVersionId), cancellationToken);
            if (response.StatusCode == HttpStatusCode.OK)
            {
                var version = await response.Content.ReadFromJsonAsync<CatalogVersion>(cancellationToken);
                version.ShouldNotBeNull();
                return version;
            }

            if (TimeProvider.System.GetElapsedTime(started) >= WaitTimeout)
            {
                throw new TimeoutException($"GET {CatalogVersionUri(courseVersionId)} still answered {(int)response.StatusCode} {response.StatusCode} after {WaitTimeout.TotalSeconds} s.");
            }

            await Task.Delay(PollInterval, cancellationToken);
        }
    }

    private static List<string> PropertyNamesIn(JsonElement element)
    {
        List<string> names = [];
        Collect(element, names);
        return names;
    }

    private static void Collect(JsonElement element, List<string> names)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                names.Add(property.Name);
                Collect(property.Value, names);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                Collect(item, names);
            }
        }
    }

    private sealed record PublishedListener(string CourseQueue, string TestQueue);

    private sealed record CatalogVersion(Guid CourseVersionId, Guid CourseId, int VersionNumber, string Title, int ValidityMonths, IReadOnlyList<CatalogChapter> Chapters);

    private sealed record CatalogChapter(Guid ChapterId, int Position, string Title, string Kind);
}
