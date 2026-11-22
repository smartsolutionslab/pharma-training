using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// An editor uploads a chapter's PDF or video straight to the module's container through a URL the edge hands out,
/// then adds or replaces the chapter with the name it was given: the module checks that the name was issued for the
/// version and that the blob is what the kind asks for before it records the chapter.
/// </summary>
[Collection(EditorialDrafts.Name)]
public class EditorialChapterUploadsTests(AspireFixture aspire)
{
    private static readonly Uri Courses = new("/editorial/courses", UriKind.Relative);

    private static readonly HttpClient Storage = new();

    private static readonly byte[] SmallPdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n");

    private static readonly byte[] SmallVideo = [0, 0, 0, 24, .. Encoding.ASCII.GetBytes("ftypmp42"), 0, 0, 0, 0, .. Encoding.ASCII.GetBytes("mp42isom")];

    public static TheoryData<int?> NoDurations { get; } = [(int?)null, 0, 14_401];

    [Fact]
    public async Task An_editor_uploads_a_pdf_adds_it_as_a_chapter_and_reads_its_file_back()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        var now = TimeProvider.System.GetUtcNow();

        var upload = await RequestUploadAsync(client, id, "Pdf", cancellationToken);
        upload.BlobName.ShouldStartWith($"course-versions/{id:D}/");
        upload.BlobName.ShouldEndWith(".pdf");
        upload.ContentType.ShouldBe("application/pdf");
        upload.MaxBytes.ShouldBe(52_428_800L);
        upload.ExpiresAt.ShouldBeInRange(now.AddMinutes(14), now.AddMinutes(16));
        await PutBlobAsync(upload, SmallPdf, cancellationToken);

        using var added = await client.PostAsJsonAsync(Chapters(id), new { title = "Hygieneplan", kind = "Pdf", file = upload.BlobName }, cancellationToken);

        await ShouldAnswerAsync(added, HttpStatusCode.Created, cancellationToken);
        var chapter = await added.Content.ReadFromJsonAsync<Guid>(cancellationToken);
        (await ReadAsync(client, id, cancellationToken)).Chapters.ShouldBe([new ChapterView(chapter, 1, "Hygieneplan", "Pdf", null, upload.BlobName, null)]);
    }

    [Fact]
    public async Task An_editor_uploads_a_video_adds_it_with_its_duration_and_reads_both_back()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);

        var upload = await RequestUploadAsync(client, id, "Video", cancellationToken);
        upload.BlobName.ShouldStartWith($"course-versions/{id:D}/");
        upload.BlobName.ShouldEndWith(".mp4");
        upload.ContentType.ShouldBe("video/mp4");
        upload.MaxBytes.ShouldBe(2_147_483_648L);
        await PutBlobAsync(upload, SmallVideo, cancellationToken);

        using var added = await client.PostAsJsonAsync(Chapters(id), new { title = "Vorführung", kind = "Video", file = upload.BlobName, durationSeconds = 90 }, cancellationToken);

        await ShouldAnswerAsync(added, HttpStatusCode.Created, cancellationToken);
        var chapter = await added.Content.ReadFromJsonAsync<Guid>(cancellationToken);
        (await ReadAsync(client, id, cancellationToken)).Chapters.ShouldBe([new ChapterView(chapter, 1, "Vorführung", "Video", null, upload.BlobName, 90)]);
    }

    [Theory]
    [MemberData(nameof(NoDurations))]
    public async Task A_video_without_a_duration_of_1_to_14400_seconds_is_refused(int? durationSeconds)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        var body = new Dictionary<string, object?> { ["title"] = "Vorführung", ["kind"] = "Video", ["file"] = $"course-versions/{id:D}/{Guid.CreateVersion7():D}.mp4" };
        if (durationSeconds is { } seconds)
        {
            body["durationSeconds"] = seconds;
        }

        using var added = await client.PostAsJsonAsync(Chapters(id), body, cancellationToken);

        await ShouldBeRefusedAsync(added, HttpStatusCode.BadRequest, "editorial.invalid-video-duration", cancellationToken);
        (await ReadAsync(client, id, cancellationToken)).Chapters.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_file_that_is_a_url_with_a_key_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        var upload = await RequestUploadAsync(client, id, "Pdf", cancellationToken);

        using var added = await client.PostAsJsonAsync(Chapters(id), new { title = "Hygieneplan", kind = "Pdf", file = upload.UploadUrl.AbsoluteUri }, cancellationToken);

        await ShouldBeRefusedAsync(added, HttpStatusCode.BadRequest, "editorial.invalid-chapter-file", cancellationToken);
    }

    [Fact]
    public async Task A_name_issued_and_uploaded_for_another_version_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        var other = await CreateAsync(client, cancellationToken);
        var upload = await RequestUploadAsync(client, other, "Pdf", cancellationToken);
        await PutBlobAsync(upload, SmallPdf, cancellationToken);

        using var added = await client.PostAsJsonAsync(Chapters(id), new { title = "Hygieneplan", kind = "Pdf", file = upload.BlobName }, cancellationToken);

        await ShouldBeRefusedAsync(added, HttpStatusCode.BadRequest, "editorial.invalid-chapter-file", cancellationToken);
        (await ReadAsync(client, id, cancellationToken)).Chapters.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_name_issued_but_never_uploaded_is_a_conflict()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        var upload = await RequestUploadAsync(client, id, "Pdf", cancellationToken);

        using var added = await client.PostAsJsonAsync(Chapters(id), new { title = "Hygieneplan", kind = "Pdf", file = upload.BlobName }, cancellationToken);

        await ShouldBeRefusedAsync(added, HttpStatusCode.Conflict, "editorial.upload-not-found", cancellationToken);
    }

    [Fact]
    public async Task An_upload_of_html_for_a_pdf_is_not_accepted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        var upload = await RequestUploadAsync(client, id, "Pdf", cancellationToken);
        await PutBlobAsync(upload, Encoding.UTF8.GetBytes("<html><body>Kein PDF</body></html>"), cancellationToken, contentType: "text/html");

        using var added = await client.PostAsJsonAsync(Chapters(id), new { title = "Hygieneplan", kind = "Pdf", file = upload.BlobName }, cancellationToken);

        await ShouldBeRefusedAsync(added, HttpStatusCode.BadRequest, "editorial.upload-not-accepted", cancellationToken);
        (await ReadAsync(client, id, cancellationToken)).Chapters.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_upload_of_html_declared_as_a_pdf_is_not_accepted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        var upload = await RequestUploadAsync(client, id, "Pdf", cancellationToken);
        await PutBlobAsync(upload, Encoding.UTF8.GetBytes("<html><body>Kein PDF</body></html>"), cancellationToken, contentType: "application/pdf");

        using var added = await client.PostAsJsonAsync(Chapters(id), new { title = "Hygieneplan", kind = "Pdf", file = upload.BlobName }, cancellationToken);

        await ShouldBeRefusedAsync(added, HttpStatusCode.BadRequest, "editorial.upload-not-accepted", cancellationToken);
        (await ReadAsync(client, id, cancellationToken)).Chapters.ShouldBeEmpty();
    }

    [Fact]
    public async Task An_upload_of_bytes_without_an_ftyp_box_declared_as_a_video_is_not_accepted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        var upload = await RequestUploadAsync(client, id, "Video", cancellationToken);
        await PutBlobAsync(upload, SmallPdf, cancellationToken, contentType: "video/mp4");

        using var added = await client.PostAsJsonAsync(Chapters(id), new { title = "Vorführung", kind = "Video", file = upload.BlobName, durationSeconds = 90 }, cancellationToken);

        await ShouldBeRefusedAsync(added, HttpStatusCode.BadRequest, "editorial.upload-not-accepted", cancellationToken);
        (await ReadAsync(client, id, cancellationToken)).Chapters.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_file_another_chapter_of_the_version_holds_is_in_use()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        var upload = await RequestUploadAsync(client, id, "Pdf", cancellationToken);
        await PutBlobAsync(upload, SmallPdf, cancellationToken);
        using var first = await client.PostAsJsonAsync(Chapters(id), new { title = "Hygieneplan", kind = "Pdf", file = upload.BlobName }, cancellationToken);
        await ShouldAnswerAsync(first, HttpStatusCode.Created, cancellationToken);
        var chapter = await first.Content.ReadFromJsonAsync<Guid>(cancellationToken);

        using var second = await client.PostAsJsonAsync(Chapters(id), new { title = "Hygieneplan, noch einmal", kind = "Pdf", file = upload.BlobName }, cancellationToken);

        await ShouldBeRefusedAsync(second, HttpStatusCode.Conflict, "editorial.file-in-use", cancellationToken);
        (await ReadAsync(client, id, cancellationToken)).Chapters.ShouldBe([new ChapterView(chapter, 1, "Hygieneplan", "Pdf", null, upload.BlobName, null)]);
    }

    [Theory]
    [InlineData("""{"kind":"Text"}""")]
    [InlineData("{}")]
    [InlineData("""{"kind":7}""")]
    public async Task An_upload_for_a_text_no_or_an_undefined_kind_is_refused(string body)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);

        using var requested = await client.PostAsync(Uploads(id), Json(body), cancellationToken);

        await ShouldBeRefusedAsync(requested, HttpStatusCode.BadRequest, "editorial.invalid-upload-kind", cancellationToken);
    }

    [Fact]
    public async Task A_chapter_of_an_undefined_kind_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);

        using var added = await client.PostAsync(Chapters(id), Json("""{"title":"Händehygiene","kind":7,"text":"Vor und nach …"}"""), cancellationToken);

        await ShouldBeRefusedAsync(added, HttpStatusCode.BadRequest, "editorial.invalid-chapter-kind", cancellationToken);
        (await ReadAsync(client, id, cancellationToken)).Chapters.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_pdf_chapter_with_text_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);

        using var added = await client.PostAsJsonAsync(
            Chapters(id),
            new { title = "Hygieneplan", kind = "Pdf", text = "Vor und nach …", file = $"course-versions/{id:D}/{Guid.CreateVersion7():D}.pdf" },
            cancellationToken);

        await ShouldBeRefusedAsync(added, HttpStatusCode.BadRequest, "editorial.chapter-content-does-not-match-kind", cancellationToken);
        (await ReadAsync(client, id, cancellationToken)).Chapters.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_published_version_gets_no_upload_url()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        await EditorialDatabase.SeedPublishedAsync(await EditorialDatabase.ConnectionStringAsync(aspire, cancellationToken), id, cancellationToken);

        using var requested = await client.PostAsJsonAsync(Uploads(id), new { kind = "Pdf" }, cancellationToken);

        await ShouldBeRefusedAsync(requested, HttpStatusCode.Conflict, "editorial.not-a-draft", cancellationToken);
    }

    [Fact]
    public async Task An_unknown_or_empty_version_gets_no_upload_url()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");

        foreach (var id in new[] { Guid.CreateVersion7(), Guid.Empty })
        {
            using var requested = await client.PostAsJsonAsync(Uploads(id), new { kind = "Pdf" }, cancellationToken);

            await ShouldBeRefusedAsync(requested, HttpStatusCode.NotFound, "editorial.course-version-not-found", cancellationToken);
        }
    }

    [Fact]
    public async Task A_text_chapter_is_replaced_in_its_place_by_an_uploaded_pdf()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        var chapter = await AddTextChapterAsync(client, id, "Händehygiene", cancellationToken);
        var after = await AddTextChapterAsync(client, id, "Flächendesinfektion", cancellationToken);
        var upload = await RequestUploadAsync(client, id, "Pdf", cancellationToken);
        await PutBlobAsync(upload, SmallPdf, cancellationToken);

        using var replaced = await client.PutAsJsonAsync(Chapter(id, chapter), new { title = "Hygieneplan", kind = "Pdf", file = upload.BlobName }, cancellationToken);

        await ShouldAnswerAsync(replaced, HttpStatusCode.OK, cancellationToken);
        (await ReadAsync(client, id, cancellationToken)).Chapters.ShouldBe(
        [
            new ChapterView(chapter, 1, "Hygieneplan", "Pdf", null, upload.BlobName, null),
            new ChapterView(after, 2, "Flächendesinfektion", "Text", "Vor und nach …", null, null),
        ]);
    }

    private static Uri Version(Guid id) => new($"/editorial/course-versions/{id}", UriKind.Relative);

    private static Uri Chapters(Guid id) => new($"/editorial/course-versions/{id}/chapters", UriKind.Relative);

    private static Uri Chapter(Guid id, Guid chapter) => new($"/editorial/course-versions/{id}/chapters/{chapter}", UriKind.Relative);

    private static Uri Uploads(Guid id) => new($"/editorial/course-versions/{id}/uploads", UriKind.Relative);

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static async Task<Guid> CreateAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var created = await client.PostAsJsonAsync(Courses, new { title = $"Hygiene {Guid.CreateVersion7()}" }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private static async Task<Guid> AddTextChapterAsync(HttpClient client, Guid id, string title, CancellationToken cancellationToken)
    {
        using var added = await client.PostAsJsonAsync(Chapters(id), new { title, text = "Vor und nach …" }, cancellationToken);
        added.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await added.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private static async Task<Upload> RequestUploadAsync(HttpClient client, Guid id, string kind, CancellationToken cancellationToken)
    {
        using var requested = await client.PostAsJsonAsync(Uploads(id), new { kind }, cancellationToken);
        await ShouldAnswerAsync(requested, HttpStatusCode.OK, cancellationToken);
        var upload = await requested.Content.ReadFromJsonAsync<Upload>(cancellationToken);
        upload.ShouldNotBeNull();
        return upload;
    }

    /// <summary>Uploads as the browser does: a PUT of a block blob with the content type the upload names, straight to the storage emulator.</summary>
    private static async Task PutBlobAsync(Upload upload, byte[] content, CancellationToken cancellationToken, string? contentType = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, upload.UploadUrl) { Content = new ByteArrayContent(content) };
        request.Headers.Add("x-ms-blob-type", "BlockBlob");
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType ?? upload.ContentType);
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

    private static async Task ShouldAnswerAsync(HttpResponseMessage response, HttpStatusCode status, CancellationToken cancellationToken) =>
        response.StatusCode.ShouldBe(status, $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} answered {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync(cancellationToken)}");

    private static async Task ShouldBeRefusedAsync(HttpResponseMessage response, HttpStatusCode status, string code, CancellationToken cancellationToken)
    {
        await ShouldAnswerAsync(response, status, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        body.ShouldNotBeNullOrWhiteSpace($"a refusal with {code} carries problem details");
        var problem = JsonSerializer.Deserialize<Problem>(body, JsonSerializerOptions.Web);
        problem.ShouldNotBeNull().Code.ShouldBe(code);
    }

    private sealed record Upload(string BlobName, Uri UploadUrl, DateTimeOffset ExpiresAt, string ContentType, long MaxBytes);

    private sealed record CourseVersion(Guid Id, string Status, IReadOnlyList<ChapterView> Chapters);

    private sealed record ChapterView(Guid Id, int Position, string Title, string Kind, string? Text, string? File, int? DurationSeconds);

    private sealed record Problem(string Code);
}
