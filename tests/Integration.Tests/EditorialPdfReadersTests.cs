using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Reading a PDF chapter's text is bounded per host (#316, ADR 0008 D11): with every reader busy, a further PDF chapter
/// is refused at once with 429 <c>editorial.pdf-readers-busy</c> and <c>Retry-After</c>, through the edge, never queued
/// behind the parses; nothing of it is recorded. Each request adds to a version of its own, so they never conflict. The service runs with its defaults, two readers and a 30-second
/// deadline, and <see cref="EditorialPdfs.SlowToRead"/> keeps a reader busy for well over the one-second wait for one.
/// </summary>
[Collection(nameof(PdfReaders))]
public class EditorialPdfReadersTests(AspireFixture aspire)
{
    private const string Busy = "editorial.pdf-readers-busy";

    private static readonly Uri Courses = new("/editorial/courses", UriKind.Relative);

    private static readonly HttpClient Storage = new();

    [Fact]
    public async Task A_pdf_beyond_the_reader_limit_is_refused_with_429_and_retry_after_not_queued()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var slow = EditorialPdfs.SlowToRead();

        // One version per request: requests that add to the same version conflict with each other (409), whatever the readers do.
        List<(Guid Version, Upload Upload)> uploads = [];
        for (var index = 0; index < 3; index++)
        {
            var version = await CreateAsync(client, cancellationToken);
            var upload = await RequestUploadAsync(client, version, cancellationToken);
            await PutBlobAsync(upload, slow, cancellationToken);
            uploads.Add((version, upload));
        }

        try
        {
            var started = TimeProvider.System.GetTimestamp();
            var answers = await Task.WhenAll(uploads.Select(upload => RecordAsync(client, upload.Version, upload.Upload, started, cancellationToken)));

            var described = string.Join("; ", answers.Select(answer => $"{answer.Status} after {answer.Elapsed.TotalSeconds:F1} s: {answer.Body}"));
            var refused = answers.Where(answer => answer.Status == (int)HttpStatusCode.TooManyRequests).ToList();
            var served = answers.Where(answer => answer.Status != (int)HttpStatusCode.TooManyRequests).ToList();
            refused.ShouldNotBeEmpty($"three slow PDFs at once with two readers: one must be refused, not queued ({described})");
            foreach (var answer in refused)
            {
                using var problem = JsonDocument.Parse(answer.Body);
                problem.RootElement.GetProperty("code").GetString().ShouldBe(Busy, described);
                problem.RootElement.GetProperty("title").GetString().ShouldBe(ErrorCodeCatalog.MessageOf(Busy), described);
                answer.RetryAfter.ShouldNotBeNull($"Retry-After in seconds ({described})").ShouldBeInRange(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5), described);
            }

            if (served.Count > 0)
            {
                refused.Max(answer => answer.Elapsed).ShouldBeLessThan(served.Min(answer => answer.Elapsed), $"refused while the others parsed, not queued behind them ({described})");
            }

            served.ShouldAllBe(answer => answer.Status == (int)HttpStatusCode.Created, described);
            for (var index = 0; index < uploads.Count; index++)
            {
                var expected = answers[index].Status == (int)HttpStatusCode.Created ? 1 : 0;
                (await ReadAsync(client, uploads[index].Version, cancellationToken)).Chapters.Count.ShouldBe(expected, $"a chapter for a 201, none for a 429 ({described})");
            }
        }
        finally
        {
            await FreeTheReadersAsync(client, await CreateAsync(client, cancellationToken), cancellationToken);
        }
    }

    private static Uri Version(Guid id) => new($"/editorial/course-versions/{id}", UriKind.Relative);

    private static Uri Chapters(Guid id) => new($"/editorial/course-versions/{id}/chapters", UriKind.Relative);

    private static Uri Uploads(Guid id) => new($"/editorial/course-versions/{id}/uploads", UriKind.Relative);

    /// <summary>Records the upload as a PDF chapter and notes how long after <paramref name="started"/> it was answered.</summary>
    private static async Task<Answer> RecordAsync(HttpClient client, Guid id, Upload upload, long started, CancellationToken cancellationToken)
    {
        using var response = await client.PostAsJsonAsync(Chapters(id), new { title = "Merkblatt", kind = "Pdf", file = upload.BlobName }, cancellationToken);
        var elapsed = TimeProvider.System.GetElapsedTime(started);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        return new Answer((int)response.StatusCode, elapsed, response.Headers.RetryAfter?.Delta, body);
    }

    /// <summary>
    /// Records a small PDF until it answers 201, retrying a 429 once its <c>Retry-After</c> has passed, for at most two
    /// minutes: the slow reads have ended then, and the tests after this one find the readers free.
    /// </summary>
    private static async Task FreeTheReadersAsync(HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        var upload = await RequestUploadAsync(client, id, cancellationToken);
        await PutBlobAsync(upload, EditorialPdfs.WithText(), cancellationToken);
        var started = TimeProvider.System.GetTimestamp();
        while (true)
        {
            var answer = await RecordAsync(client, id, upload, started, cancellationToken);
            if (answer.Status == (int)HttpStatusCode.Created)
            {
                return;
            }

            answer.Status.ShouldBe((int)HttpStatusCode.TooManyRequests, $"freeing the readers: {answer.Body}");
            answer.Elapsed.ShouldBeLessThan(TimeSpan.FromMinutes(2), "the PDF readers were still busy two minutes later");
            await Task.Delay(answer.RetryAfter ?? TimeSpan.FromSeconds(5), cancellationToken);
        }
    }

    private static async Task<Guid> CreateAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var created = await client.PostAsJsonAsync(Courses, new { title = $"Hygiene {Guid.CreateVersion7()}" }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private static async Task<Upload> RequestUploadAsync(HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        using var requested = await client.PostAsJsonAsync(Uploads(id), new { kind = "Pdf" }, cancellationToken);
        requested.StatusCode.ShouldBe(HttpStatusCode.OK, await requested.Content.ReadAsStringAsync(cancellationToken));
        var upload = await requested.Content.ReadFromJsonAsync<Upload>(cancellationToken);
        upload.ShouldNotBeNull();
        return upload;
    }

    /// <summary>Uploads as the browser does: a PUT of a block blob with the content type the upload names, straight to the storage emulator.</summary>
    private static async Task PutBlobAsync(Upload upload, byte[] content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, upload.UploadUrl) { Content = new ByteArrayContent(content) };
        request.Headers.Add("x-ms-blob-type", "BlockBlob");
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(upload.ContentType);
        using var put = await Storage.SendAsync(request, cancellationToken);
        put.StatusCode.ShouldBe(HttpStatusCode.Created, await put.Content.ReadAsStringAsync(cancellationToken));
    }

    private static async Task<CourseVersion> ReadAsync(HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(Version(id), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var version = await response.Content.ReadFromJsonAsync<CourseVersion>(cancellationToken);
        version.ShouldNotBeNull();
        return version;
    }

    private sealed record Answer(int Status, TimeSpan Elapsed, TimeSpan? RetryAfter, string Body);

    private sealed record Upload(string BlobName, Uri UploadUrl, DateTimeOffset ExpiresAt, string ContentType, long MaxBytes);

    private sealed record CourseVersion(Guid Id, IReadOnlyList<ChapterView> Chapters);

    private sealed record ChapterView(Guid Id);
}
