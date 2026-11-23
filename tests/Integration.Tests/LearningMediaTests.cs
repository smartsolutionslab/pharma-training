using System.Net;
using System.Net.Http.Json;
using System.Text;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.EditorialWorkflow;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningDatabase;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A learner reads a chapter's PDF or video through the edge (#41): <c>GET /learning/media/{chapterId}</c> answers a
/// short-lived read URL on Learning's own copy of the file, once the caller has a session on a version that holds the
/// chapter. Editorial releases the file when it publishes the version, and Learning copies it into its own container.
/// </summary>
[Collection(EditorialDrafts.Name)]
public class LearningMediaTests(AspireFixture aspire)
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    /// <summary>Reads the URL Learning signed straight from the storage emulator, as the learner's browser does.</summary>
    private static readonly HttpClient Storage = new();

    [Fact]
    public async Task Without_a_token_it_answers_401()
    {
        using var client = aspire.CreateHostClient();

        using var response = await client.GetAsync(Media(Guid.CreateVersion7()), TestContext.Current.CancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Someone_without_a_session_on_the_version_gets_403()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = Published(Guid.CreateVersion7());
        await DeliverAsync(aspire, published, cancellationToken);
        var pdf = published.Chapters.Single(chapter => chapter.Kind == "Pdf");
        using var visitor = await aspire.CreateHostClientAsync("visitor");

        using var response = await visitor.GetAsync(Media(pdf.ChapterId), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(response, cancellationToken)).ShouldBe("learning.no-session-on-version");
    }

    [Fact]
    public async Task An_unknown_chapter_answers_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");

        using var response = await client.GetAsync(Media(Guid.CreateVersion7()), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(response, cancellationToken)).ShouldBe("learning.chapter-not-found");
    }

    /// <summary>
    /// End to end: Editorial publishes a version with a PDF chapter, Learning receives the version and copies the file
    /// into its own container, and the learner's URL serves the bytes the editor uploaded, inline as a PDF, from
    /// Learning's container, not Editorial's.
    /// </summary>
    [Fact]
    public async Task A_learner_with_a_session_gets_a_url_that_serves_the_published_pdf_inline()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var bytes = Encoding.ASCII.GetBytes($"%PDF-1.4\n% {Guid.CreateVersion7()}\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n");
        using var editor = await aspire.CreateHostClientAsync("editor");
        using var learner = await aspire.CreateHostClientAsync("employee-a");
        var (versionId, chapterId) = await PublishedWithPdfChapterAsync(editor, bytes, cancellationToken);

        // Learning holds the version once its consumer took Editorial's announcement; until then the start answers 404.
        using var started = await WaitUntilAsync(() => StartAsync(learner, versionId, cancellationToken), HttpStatusCode.Created, $"POST {Sessions} for version {versionId}", cancellationToken);

        // The file is Learning's once its consumer copied it; until then the media answers 409 learning.media-not-ready.
        MediaBody answer;
        using (var media = await WaitUntilAsync(() => learner.GetAsync(Media(chapterId), cancellationToken), HttpStatusCode.OK, $"GET {Media(chapterId)}", cancellationToken))
        {
            answer = (await media.Content.ReadFromJsonAsync<MediaBody>(cancellationToken)).ShouldNotBeNull();
        }

        answer.ContentType.ShouldBe("application/pdf");
        answer.Kind.ShouldBe("Pdf");
        answer.ExpiresAt.ShouldBeGreaterThan(TimeProvider.System.GetUtcNow());
        answer.ExpiresAt.ShouldBeLessThanOrEqualTo(TimeProvider.System.GetUtcNow().AddMinutes(15));
        answer.Url.AbsolutePath.ShouldContain("/learning-blobs/");
        answer.Url.AbsoluteUri.ShouldNotContain("editorial-blobs");
        using var read = await Storage.GetAsync(answer.Url, cancellationToken);
        read.StatusCode.ShouldBe(HttpStatusCode.OK, await read.Content.ReadAsStringAsync(cancellationToken));
        read.Content.Headers.ContentType.ShouldNotBeNull().MediaType.ShouldBe("application/pdf");
        read.Content.Headers.ContentDisposition.ShouldNotBeNull().DispositionType.ShouldBe("inline");
        (await read.Content.ReadAsByteArrayAsync(cancellationToken)).ShouldBe(bytes);
    }

    [Fact]
    public async Task A_text_chapter_answers_404_chapter_has_no_media()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = Published(Guid.CreateVersion7());
        await DeliverAsync(aspire, published, cancellationToken);
        var text = published.Chapters.Single(chapter => chapter.Kind == "Text");
        using var client = await aspire.CreateHostClientAsync("employee-a");
        using (var started = await StartAsync(client, published.CourseVersionId, cancellationToken))
        {
            started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));
        }

        using var response = await client.GetAsync(Media(text.ChapterId), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(response, cancellationToken)).ShouldBe("learning.chapter-has-no-media");
    }

    private static Uri Media(Guid chapterId) => new($"/learning/media/{chapterId}", UriKind.Relative);

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

    private sealed record MediaBody(Uri Url, DateTimeOffset ExpiresAt, string ContentType, string Kind);
}
