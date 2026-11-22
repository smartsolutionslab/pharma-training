using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// One file is the file of one chapter of a version, and the editorial database says so too, not only the module: a
/// unique index refuses a second chapter of the version holding the same file, while chapters of other versions (a
/// chapter copied into the next version keeps its file) and chapters without a file are not limited. A request that
/// checked the version before another writer recorded the same file loses at the index and answers 409
/// <c>already-exists</c>, never a server error and never a second chapter.
/// </summary>
[Collection(EditorialDrafts.Name)]
public class EditorialChapterFilesTests(AspireFixture aspire)
{
    private const string BlockedByGate = "SELECT count(*) FROM pg_stat_activity WHERE @gate = ANY(pg_blocking_pids(pid))";

    private static readonly Uri Courses = new("/editorial/courses", UriKind.Relative);

    private static readonly HttpClient Storage = new();

    private static readonly byte[] SmallPdf = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n");

    [Fact]
    public async Task The_editorial_database_refuses_a_second_chapter_of_a_version_holding_the_same_file()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var connectionString = await EditorialDatabase.ConnectionStringAsync(aspire, cancellationToken);
        var version = await CreateAsync(client, cancellationToken);
        var next = await CreateAsync(client, cancellationToken);
        var file = $"course-versions/{version:D}/{Guid.CreateVersion7():D}.pdf";
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);

        await InsertChapterAsync(connection, null, version, 1, "Pdf", file, cancellationToken);
        await InsertChapterAsync(connection, null, next, 1, "Pdf", file, cancellationToken);
        await InsertChapterAsync(connection, null, version, 2, "Text", null, cancellationToken);
        await InsertChapterAsync(connection, null, version, 3, "Text", null, cancellationToken);
        var refused = await Should.ThrowAsync<PostgresException>(
            () => InsertChapterAsync(connection, null, version, 4, "Pdf", file, cancellationToken),
            "a second chapter of the version holding the same file must be refused by a unique index, not only by the module");

        refused.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
    }

    [Fact]
    public async Task A_chapter_added_with_a_file_another_writer_recorded_after_the_version_was_checked_answers_409_already_exists()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var connectionString = await EditorialDatabase.ConnectionStringAsync(aspire, cancellationToken);
        var id = await CreateAsync(client, cancellationToken);
        var upload = await RequestUploadAsync(client, id, cancellationToken);
        await PutBlobAsync(upload, SmallPdf, cancellationToken);

        // The other writer records the file in a transaction it has not committed: the request loads the version without
        // it, so the module's own check passes, and the request's insert of the same file waits on the index for the
        // other writer. Once that commits, the index refuses the request's chapter. Without the index nothing waits.
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var winner = await InsertChapterAsync(connection, transaction, id, 1, "Pdf", upload.BlobName, cancellationToken);
        Task<HttpResponseMessage> added;
        bool waited;
        await using (var gate = new VersionLock(connection, transaction))
        {
            added = client.PostAsJsonAsync(Chapters(id), new { title = "Hygieneplan", kind = "Pdf", file = upload.BlobName }, cancellationToken);
            waited = await WaitUntilBlockedOrDoneAsync(connectionString, gate, added, cancellationToken);
            await gate.CommitAsync(cancellationToken);
        }

        using var response = await added;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var described = $"POST chapter answered {(int)response.StatusCode} ({body}); it {(waited ? "waited" : "never waited")} for the other writer's chapter";
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, described);
        JsonSerializer.Deserialize<Problem>(body, JsonSerializerOptions.Web).ShouldNotBeNull(described).Code.ShouldBe("already-exists", described);
        (await ReadAsync(client, id, cancellationToken)).Chapters.Select(chapter => chapter.Id).ShouldBe([winner], "only the other writer's chapter holds the file");
    }

    private static Uri Version(Guid id) => new($"/editorial/course-versions/{id}", UriKind.Relative);

    private static Uri Chapters(Guid id) => new($"/editorial/course-versions/{id}/chapters", UriKind.Relative);

    private static Uri Uploads(Guid id) => new($"/editorial/course-versions/{id}/uploads", UriKind.Relative);

    private static async Task<Guid> InsertChapterAsync(
        NpgsqlConnection connection, NpgsqlTransaction? transaction, Guid version, int position, string kind, string? file, CancellationToken cancellationToken)
    {
        var id = Guid.CreateVersion7();
        await using var command = new NpgsqlCommand(
            "INSERT INTO editorial.course_version_chapters (id, course_version_id, position, title, kind, text, file) VALUES (@id, @version, @position, @title, @kind, @text, @file)",
            connection,
            transaction);
        command.Parameters.AddWithValue("id", id);
        command.Parameters.AddWithValue("version", version);
        command.Parameters.AddWithValue("position", position);
        command.Parameters.AddWithValue("title", $"Kapitel {position}");
        command.Parameters.AddWithValue("kind", kind);
        command.Parameters.AddWithValue("text", file is null ? "Vor und nach …" : (object)DBNull.Value);
        command.Parameters.AddWithValue("file", (object?)file ?? DBNull.Value);
        (await command.ExecuteNonQueryAsync(cancellationToken)).ShouldBe(1);
        return id;
    }

    /// <summary>
    /// Polls until a backend waits for the gate, or the request has answered without waiting, for at most 30 seconds;
    /// true when one waited.
    /// </summary>
    private static async Task<bool> WaitUntilBlockedOrDoneAsync(string connectionString, VersionLock gate, Task request, CancellationToken cancellationToken)
    {
        var started = TimeProvider.System.GetTimestamp();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(BlockedByGate, connection);
        command.Parameters.AddWithValue("gate", gate.ProcessId);
        while (!request.IsCompleted)
        {
            TimeProvider.System.GetElapsedTime(started).ShouldBeLessThan(TimeSpan.FromSeconds(30), "the request neither waited for the other writer nor answered");
            if ((long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L) > 0)
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }

        return false;
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

    private sealed record Upload(string BlobName, Uri UploadUrl, DateTimeOffset ExpiresAt, string ContentType, long MaxBytes);

    private sealed record CourseVersion(Guid Id, IReadOnlyList<ChapterView> Chapters);

    private sealed record ChapterView(Guid Id, string? File);

    private sealed record Problem(string Code);
}
