using System.Net;
using System.Net.Http.Json;
using Npgsql;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Two editors change one draft at once: a question one removes while the other edits it, a chapter one removes while
/// the other adds a question to it. Both load the version before either writes; the one that writes second is told the
/// version changed (or, adding to a chapter that is gone, that the chapter is not found), never a server error, and the
/// version is read back as the winner left it. Each round lets a different request through first.
/// </summary>
public class EditorialQuestionRacesTests(AspireFixture aspire)
{
    private const int Rounds = 4;

    private static readonly Uri Courses = new("/editorial/courses", UriKind.Relative);

    [Fact]
    public async Task Of_a_removal_and_an_edit_of_one_question_one_succeeds_and_the_other_is_told_the_version_changed_never_a_server_error()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var connectionString = await EditorialDatabase.ConnectionStringAsync(aspire, cancellationToken);
        List<string> wrong = [];

        for (var round = 1; round <= Rounds; round++)
        {
            var id = await CreateAsync(client, cancellationToken);
            var chapter = await AddChapterAsync(client, id, cancellationToken);
            var question = await AddQuestionAsync(client, id, SingleChoice(chapter), cancellationToken);
            var removalFirst = round % 2 == 1;

            var (removal, edit) = await RaceAsync(
                connectionString,
                id,
                () => client.DeleteAsync(Question(id, question), cancellationToken),
                () => client.PutAsJsonAsync(Question(id, question), MultipleChoice(chapter), cancellationToken),
                removalFirst,
                cancellationToken);

            using (removal)
            using (edit)
            {
                var described = $"round {round}, {(removalFirst ? "removal" : "edit")} let through first: DELETE answered {(int)removal.StatusCode}, PUT answered {(int)edit.StatusCode}";
                var outcome = await OutcomeAsync(removal, edit, HttpStatusCode.OK, HttpStatusCode.OK, [], cancellationToken);
                if (outcome is not { } removalWon)
                {
                    wrong.Add($"{described}; expected one 200 and one 409 editorial.course-version-changed ({await CodesAsync(removal, edit, cancellationToken)}).");
                    continue;
                }

                var questions = await QuestionsOfAsync(client, id, cancellationToken);
                var options = await OptionCountAsync(connectionString, question, cancellationToken);
                if (removalWon && (questions.Count != 0 || options != 0))
                {
                    wrong.Add($"{described}; the removal won but {questions.Count} question(s) and {options} option(s) are left.");
                }

                if (!removalWon && (questions.Count != 1 || questions[0].Id != question || questions[0].Type != "MultipleChoice" || options != 3))
                {
                    wrong.Add($"{described}; the edit won but the version holds {questions.Count} question(s) and {options} option(s) of the edited one.");
                }
            }
        }

        wrong.ShouldBeEmpty();
    }

    [Fact]
    public async Task Of_a_removal_of_a_chapter_and_a_question_added_to_it_one_succeeds_and_the_other_is_refused_never_a_server_error()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var connectionString = await EditorialDatabase.ConnectionStringAsync(aspire, cancellationToken);
        List<string> wrong = [];

        for (var round = 1; round <= Rounds; round++)
        {
            var id = await CreateAsync(client, cancellationToken);
            var chapter = await AddChapterAsync(client, id, cancellationToken);
            var removalFirst = round % 2 == 1;

            var (removal, addition) = await RaceAsync(
                connectionString,
                id,
                () => client.DeleteAsync(Chapter(id, chapter), cancellationToken),
                () => client.PostAsJsonAsync(Questions(id), SingleChoice(chapter), cancellationToken),
                removalFirst,
                cancellationToken);

            using (removal)
            using (addition)
            {
                var described = $"round {round}, {(removalFirst ? "removal" : "addition")} let through first: DELETE chapter answered {(int)removal.StatusCode}, POST question answered {(int)addition.StatusCode}";
                var outcome = await OutcomeAsync(removal, addition, HttpStatusCode.OK, HttpStatusCode.Created, ["editorial.chapter-not-found"], cancellationToken);
                if (outcome is not { } removalWon)
                {
                    wrong.Add($"{described}; expected one success and the other 409 editorial.course-version-changed (or 404 editorial.chapter-not-found for the POST) ({await CodesAsync(removal, addition, cancellationToken)}).");
                    continue;
                }

                var version = await ReadAsync(client, id, cancellationToken);
                var questions = version.Questions ?? [];
                if (removalWon && (version.Chapters.Count != 0 || questions.Count != 0))
                {
                    wrong.Add($"{described}; the removal won but {version.Chapters.Count} chapter(s) and {questions.Count} question(s) are left.");
                }

                if (!removalWon && (version.Chapters.Count != 1 || questions.Count != 1 || questions[0].ChapterId != chapter))
                {
                    wrong.Add($"{described}; the addition won but the version holds {version.Chapters.Count} chapter(s) and {questions.Count} question(s).");
                }
            }
        }

        wrong.ShouldBeEmpty();
    }

    /// <summary>
    /// Holds the version, its chapters, questions and options, lets one request and then the other load the version
    /// and wait on those rows, and releases them by rolling back: the request let through first is first in line.
    /// </summary>
    private static async Task<(HttpResponseMessage Removal, HttpResponseMessage Other)> RaceAsync(
        string connectionString, Guid id, Func<Task<HttpResponseMessage>> removal, Func<Task<HttpResponseMessage>> other, bool removalFirst, CancellationToken cancellationToken)
    {
        Task<HttpResponseMessage> removed;
        Task<HttpResponseMessage> changed;
        await using (var gate = await LockAsync(connectionString, id, cancellationToken))
        {
            if (removalFirst)
            {
                removed = removal();
                await WaitUntilBlockedAsync(connectionString, 1, cancellationToken);
                changed = other();
            }
            else
            {
                changed = other();
                await WaitUntilBlockedAsync(connectionString, 1, cancellationToken);
                removed = removal();
            }

            await WaitUntilBlockedAsync(connectionString, 2, cancellationToken);
            await gate.RollbackAsync(cancellationToken);
        }

        var responses = await Task.WhenAll(removed, changed);
        return (responses[0], responses[1]);
    }

    /// <summary>
    /// Whether the removal won (true) or the other request did (false), when exactly one succeeded and the other was
    /// told the version changed or, for the other request, one of the codes it may also answer; null otherwise.
    /// </summary>
    private static async Task<bool?> OutcomeAsync(
        HttpResponseMessage removal, HttpResponseMessage other, HttpStatusCode removed, HttpStatusCode succeeded, string[] otherMayAnswer, CancellationToken cancellationToken)
    {
        if (removal.StatusCode == removed && await IsChangedAsync(other, otherMayAnswer, cancellationToken))
        {
            return true;
        }

        if (other.StatusCode == succeeded && await IsChangedAsync(removal, [], cancellationToken))
        {
            return false;
        }

        return null;
    }

    private static async Task<bool> IsChangedAsync(HttpResponseMessage response, string[] mayAnswer, CancellationToken cancellationToken)
    {
        if (response.StatusCode is not (HttpStatusCode.Conflict or HttpStatusCode.NotFound))
        {
            return false;
        }

        var code = await CodeOfAsync(response, cancellationToken);
        return response.StatusCode == HttpStatusCode.Conflict
            ? code == "editorial.course-version-changed"
            : mayAnswer.Contains(code);
    }

    private static async Task<string> CodesAsync(HttpResponseMessage first, HttpResponseMessage second, CancellationToken cancellationToken) =>
        $"bodies: {await first.Content.ReadAsStringAsync(cancellationToken)} | {await second.Content.ReadAsStringAsync(cancellationToken)}";

    private static async Task<VersionLock> LockAsync(string connectionString, Guid id, CancellationToken cancellationToken)
    {
        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            var transaction = await connection.BeginTransactionAsync(cancellationToken);
            string[] locks =
            [
                "SELECT 1 FROM editorial.course_versions WHERE id = @id FOR UPDATE",
                "SELECT 1 FROM editorial.course_version_chapters WHERE course_version_id = @id FOR UPDATE",
                "SELECT 1 FROM editorial.course_version_questions WHERE course_version_id = @id FOR UPDATE",
                "SELECT 1 FROM editorial.course_version_answer_options WHERE question_id IN (SELECT id FROM editorial.course_version_questions WHERE course_version_id = @id) FOR UPDATE",
            ];
            foreach (var sql in locks)
            {
                await using var command = new NpgsqlCommand(sql, connection, transaction);
                command.Parameters.AddWithValue("id", id);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            return new VersionLock(connection, transaction);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    /// <summary>Polls until at least the given number of statements on course versions wait for a lock, for at most 30 seconds.</summary>
    private static async Task WaitUntilBlockedAsync(string connectionString, int expected, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(
            """
            SELECT count(*) FROM pg_stat_activity
            WHERE wait_event_type = 'Lock' AND datname = current_database() AND pid <> pg_backend_pid()
              AND query ILIKE '%course_version%'
            """,
            connection);

        var blocked = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
        while (blocked < expected)
        {
            if (timeout.IsCancellationRequested)
            {
                blocked.ShouldBeGreaterThanOrEqualTo(expected, "the concurrent requests never waited for the locks on the version");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
            blocked = (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
        }
    }

    private static async Task<long> OptionCountAsync(string connectionString, Guid question, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("SELECT count(*) FROM editorial.course_version_answer_options WHERE question_id = @question", connection);
        command.Parameters.AddWithValue("question", question);
        return (long)(await command.ExecuteScalarAsync(cancellationToken) ?? 0L);
    }

    private static Uri Questions(Guid id) => new($"/editorial/course-versions/{id}/questions", UriKind.Relative);

    private static Uri Question(Guid id, Guid question) => new($"/editorial/course-versions/{id}/questions/{question}", UriKind.Relative);

    private static Uri Chapter(Guid id, Guid chapter) => new($"/editorial/course-versions/{id}/chapters/{chapter}", UriKind.Relative);

    private static object SingleChoice(Guid chapter) =>
        Body(chapter, "Wann desinfizieren Sie die Hände?", "SingleChoice", ("Vor der Rezeptur", true), ("Einmal am Tag", false));

    private static object MultipleChoice(Guid chapter) =>
        Body(chapter, "Welche Flächen werden desinfiziert?", "MultipleChoice", ("Rezepturtisch", true), ("Waage", true), ("Schaufenster", false));

    private static object Body(Guid chapter, string text, string type, params (string Text, bool Correct)[] options) =>
        new { chapterId = chapter, text, type, options = options.Select(option => new { text = option.Text, correct = option.Correct }).ToList() };

    private static async Task<Guid> CreateAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var created = await client.PostAsJsonAsync(Courses, new { title = $"Hygiene {Guid.CreateVersion7()}" }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private static async Task<Guid> AddChapterAsync(HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        using var added = await client.PostAsJsonAsync(new Uri($"/editorial/course-versions/{id}/chapters", UriKind.Relative), new { title = "Händehygiene", text = "Vor und nach …" }, cancellationToken);
        added.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await added.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private static async Task<Guid> AddQuestionAsync(HttpClient client, Guid id, object question, CancellationToken cancellationToken)
    {
        using var added = await client.PostAsJsonAsync(Questions(id), question, cancellationToken);
        added.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await added.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private static async Task<VersionView> ReadAsync(HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri($"/editorial/course-versions/{id}", UriKind.Relative), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var version = await response.Content.ReadFromJsonAsync<VersionView>(cancellationToken);
        version.ShouldNotBeNull();
        return version;
    }

    private static async Task<IReadOnlyList<QuestionView>> QuestionsOfAsync(HttpClient client, Guid id, CancellationToken cancellationToken) =>
        (await ReadAsync(client, id, cancellationToken)).Questions ?? [];

    private static async Task<string> CodeOfAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var problem = await response.Content.ReadFromJsonAsync<Problem>(cancellationToken);
        return problem?.Code ?? string.Empty;
    }

    private sealed record VersionView(Guid Id, IReadOnlyList<ChapterView> Chapters, IReadOnlyList<QuestionView>? Questions);

    private sealed record ChapterView(Guid Id);

    private sealed record QuestionView(Guid Id, Guid ChapterId, string Type);

    private sealed record Problem(string Code);
}
