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
[Collection(EditorialDrafts.Name)]
public class EditorialQuestionRacesTests(AspireFixture aspire)
{
    private const int Rounds = 4;

    private const int DeadlockRounds = 3;

    /// <summary>A request whose row version moved on is told the version changed: load it again.</summary>
    private const string Changed = "editorial.course-version-changed";

    /// <summary>A request PostgreSQL ended as a deadlock is told another save came at the same moment: try again.</summary>
    private const string ConcurrentSave = "editorial.concurrent-save";

    /// <summary>
    /// How the loser of a load race may be refused: on its row version, or, where PostgreSQL ended it first as a deadlock
    /// (seen when the removal is let through first), told to try again. Either way nothing of it was saved.
    /// </summary>
    private static readonly string[] LostEitherWay = [Changed, ConcurrentSave];

    private static readonly Uri Courses = new("/editorial/courses", UriKind.Relative);

    [Fact]
    public async Task Of_a_removal_and_an_edit_of_one_question_one_succeeds_and_the_other_is_refused_never_a_server_error()
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
                    wrong.Add($"{described}; expected one 200 and one 409 {Changed} or {ConcurrentSave} ({await CodesAsync(removal, edit, cancellationToken)}).");
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
                    wrong.Add($"{described}; expected one success and the other 409 {Changed} or {ConcurrentSave} (or 404 editorial.chapter-not-found for the POST) ({await CodesAsync(removal, addition, cancellationToken)}).");
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

    [Fact]
    public async Task A_question_added_to_a_chapter_whose_removal_deadlocks_with_it_answers_409_or_201_never_a_server_error()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var connectionString = await EditorialDatabase.ConnectionStringAsync(aspire, cancellationToken);
        List<string> wrong = [];

        // The addition saves INSERT question, UPDATE version, INSERT options, and checks the deferred question->chapter
        // key at COMMIT (FOR KEY SHARE on the chapter); the removal saves DELETE chapter, UPDATE version. That is read
        // from PostgreSQL's statement log, not assumed. Holding the version's row makes both wait on it, and once the
        // gate ends, whichever backend gets there first updates it: often the removal, which then commits, and the
        // addition loses on its row version without a deadlock. So the gate holds no row both need. It locks the
        // options' table against writes (SHARE), which only the addition writes, and only after its version UPDATE:
        //   1. the addition inserts the question, updates the version and waits to insert its options, for the gate;
        //   2. the removal deletes the chapter and waits to update the version, for the addition, so behind the gate;
        //   3. the gate rolls back: the addition inserts its options and, at COMMIT, waits for the deleted chapter,
        //      for the removal, which waits for the addition.
        // PostgreSQL then ends one of the two as a deadlock (40P01) every round, and the deadlock count rises each round.
        for (var round = 1; round <= DeadlockRounds; round++)
        {
            var id = await CreateAsync(client, cancellationToken);
            var chapter = await AddChapterAsync(client, id, cancellationToken);

            var deadlocksBefore = await EditorialDatabase.SettledDeadlocksAsync(connectionString, cancellationToken);
            Task<HttpResponseMessage> added;
            Task<HttpResponseMessage> removed;
            await using (var gate = await EditorialDatabase.LockAnswerOptionsAsync(connectionString, cancellationToken))
            {
                added = client.PostAsJsonAsync(Questions(id), SingleChoice(chapter), cancellationToken);
                await EditorialDatabase.WaitUntilBlockedAsync(connectionString, gate, 1, cancellationToken);
                removed = client.DeleteAsync(Chapter(id, chapter), cancellationToken);
                await EditorialDatabase.WaitUntilBlockedAsync(connectionString, gate, 2, cancellationToken);
                await gate.RollbackAsync(cancellationToken);
            }

            using var removal = await removed;
            using var addition = await added;
            var described = $"round {round}: DELETE chapter answered {(int)removal.StatusCode}, POST question answered {(int)addition.StatusCode}";
            if (await OutcomeAsync(removal, addition, HttpStatusCode.OK, HttpStatusCode.Created, [], [ConcurrentSave], cancellationToken) is null)
            {
                wrong.Add($"{described}; expected one success and the other 409 {ConcurrentSave} ({await CodesAsync(removal, addition, cancellationToken)}).");
            }

            await EditorialDatabase.ShouldHaveDeadlockedSinceAsync(connectionString, deadlocksBefore, cancellationToken);
            TestContext.Current.TestOutputHelper?.WriteLine($"{described}; deadlocks {deadlocksBefore} -> {await EditorialDatabase.DeadlocksAsync(connectionString, cancellationToken)}.");
        }

        wrong.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_question_added_to_a_chapter_whose_removal_deadlocks_with_it_at_commit_is_told_to_try_again_never_a_server_error()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var connectionString = await EditorialDatabase.ConnectionStringAsync(aspire, cancellationToken);
        List<string> wrong = [];

        // The same gate as above, held well past deadlock_timeout once the removal waits. PostgreSQL checks a lock wait
        // for a deadlock once, deadlock_timeout after it starts: the removal's check runs while the addition still waits
        // for the gate, finds no cycle and is not repeated. Once the gate rolls back, the addition closes the cycle at
        // COMMIT (its deferred question->chapter key check waits for the deleted chapter) and is the one ended, every
        // round. The removal then updates the version and commits; the addition lost the race on the version. The gate
        // holds three times the database's own deadlock_timeout, read rather than assumed, so the removal's check has run
        // before the gate ends on a loaded runner or under a higher setting too; held only just past it, the removal's
        // check could still be due once the addition closes the cycle, and the removal would be the one ended.
        var held = await EditorialDatabase.DeadlockTimeoutAsync(connectionString, cancellationToken) * 3;
        for (var round = 1; round <= DeadlockRounds; round++)
        {
            var id = await CreateAsync(client, cancellationToken);
            var chapter = await AddChapterAsync(client, id, cancellationToken);

            var deadlocksBefore = await EditorialDatabase.SettledDeadlocksAsync(connectionString, cancellationToken);
            Task<HttpResponseMessage> added;
            Task<HttpResponseMessage> removed;
            await using (var gate = await EditorialDatabase.LockAnswerOptionsAsync(connectionString, cancellationToken))
            {
                added = client.PostAsJsonAsync(Questions(id), SingleChoice(chapter), cancellationToken);
                await EditorialDatabase.WaitUntilBlockedAsync(connectionString, gate, 1, cancellationToken);
                removed = client.DeleteAsync(Chapter(id, chapter), cancellationToken);
                await EditorialDatabase.WaitUntilBlockedAsync(connectionString, gate, 2, cancellationToken);
                await Task.Delay(held, cancellationToken);
                await gate.RollbackAsync(cancellationToken);
            }

            using var removal = await removed;
            using var addition = await added;
            var described = $"round {round}: DELETE chapter answered {(int)removal.StatusCode}, POST question answered {(int)addition.StatusCode}";
            if (removal.StatusCode != HttpStatusCode.OK || !await IsChangedAsync(addition, [], [ConcurrentSave], cancellationToken))
            {
                wrong.Add($"{described}; expected 200 for the removal and 409 {ConcurrentSave} for the addition ({await CodesAsync(removal, addition, cancellationToken)}).");
            }

            await EditorialDatabase.ShouldHaveDeadlockedSinceAsync(connectionString, deadlocksBefore, cancellationToken);
            TestContext.Current.TestOutputHelper?.WriteLine($"{described}; deadlocks {deadlocksBefore} -> {await EditorialDatabase.DeadlocksAsync(connectionString, cancellationToken)}.");
        }

        wrong.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_version_read_while_a_question_and_its_options_change_is_read_back_as_one_snapshot()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var connectionString = await EditorialDatabase.ConnectionStringAsync(aspire, cancellationToken);
        var id = await CreateAsync(client, cancellationToken);
        var chapter = await AddChapterAsync(client, id, cancellationToken);
        var question = await AddQuestionAsync(client, id, SingleChoice(chapter), cancellationToken);

        // The read loads the version, its chapters, its questions and their options in one query each. The gate locks
        // the options' table, so the read has read the question when it waits for the options; the gate then changes
        // the question's text and its options' texts and commits. Read in one snapshot, both are as they were.
        Task<HttpResponseMessage> read;
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var gate = new VersionLock(connection, transaction))
        {
            await using (var locked = new NpgsqlCommand("LOCK TABLE editorial.course_version_answer_options IN ACCESS EXCLUSIVE MODE", connection, transaction))
            {
                await locked.ExecuteNonQueryAsync(cancellationToken);
            }

            read = client.GetAsync(new Uri($"/editorial/course-versions/{id}", UriKind.Relative), cancellationToken);
            await EditorialDatabase.WaitUntilBlockedAsync(connectionString, gate, 1, cancellationToken);
            await using (var changed = new NpgsqlCommand(
                """
                UPDATE editorial.course_version_questions SET text = 'Geändert' WHERE id = @question;
                UPDATE editorial.course_version_answer_options SET text = 'Geändert' WHERE question_id = @question;
                """,
                connection,
                transaction))
            {
                changed.Parameters.AddWithValue("question", question);
                await changed.ExecuteNonQueryAsync(cancellationToken);
            }

            await gate.CommitAsync(cancellationToken);
        }

        using var response = await read;
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var version = await response.Content.ReadFromJsonAsync<VersionView>(cancellationToken);
        var readBack = (version?.Questions ?? []).ShouldHaveSingleItem();
        readBack.Text.ShouldBe("Wann desinfizieren Sie die Hände?");
        readBack.Options.Select(option => option.Text).ShouldBe(["Vor der Rezeptur", "Einmal am Tag"], "the options were read in another snapshot than their question");
    }

    [Theory]
    [InlineData("chapters")]
    [InlineData("questions")]
    public async Task An_addition_whose_load_sees_another_editors_question_land_between_its_queries_is_told_the_version_changed_never_a_server_error(string added)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var connectionString = await EditorialDatabase.ConnectionStringAsync(aspire, cancellationToken);
        var id = await CreateAsync(client, cancellationToken);
        var chapter = await AddChapterAsync(client, id, cancellationToken);
        var question = await AddQuestionAsync(client, id, SingleChoice(chapter), cancellationToken);
        var landed = Guid.CreateVersion7();

        // A change loads the version, its chapters, its questions and their options in one query each. The gate locks
        // the options' table, so the change has read the version (and its row version) and the one question when it
        // waits for the options. The gate then adds a second question with its options, as another editor's addition
        // would (copying the first, so every column is one the API writes), updates the version as that addition does,
        // and commits. The options the change then reads belong to a question its own query never saw: the version
        // changed under it, and it lost the race to the other editor. Whatever it loads, it must not answer 500.
        Task<HttpResponseMessage> change;
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using (var gate = new VersionLock(connection, transaction))
        {
            await using (var locked = new NpgsqlCommand("LOCK TABLE editorial.course_version_answer_options IN ACCESS EXCLUSIVE MODE", connection, transaction))
            {
                await locked.ExecuteNonQueryAsync(cancellationToken);
            }

            change = added == "chapters"
                ? client.PostAsJsonAsync(new Uri($"/editorial/course-versions/{id}/chapters", UriKind.Relative), new { title = "Flächendesinfektion", text = "Täglich …" }, cancellationToken)
                : client.PostAsJsonAsync(Questions(id), SingleChoice(chapter), cancellationToken);
            await EditorialDatabase.WaitUntilBlockedAsync(connectionString, gate, 1, cancellationToken);
            await using (var landing = new NpgsqlCommand(
                """
                INSERT INTO editorial.course_version_questions (id, chapter_id, course_version_id, position, provenance, origin, edit_history, approval_history, review, reviewed_at, reviewed_by, text, type)
                SELECT @landed, chapter_id, course_version_id, position + 1, provenance, 'HandWritten', 'Untouched', CASE WHEN review = 'Approved' THEN 'ApprovedOnce' ELSE 'NeverApproved' END, review, reviewed_at, reviewed_by, 'Wie lange wirkt das Mittel ein?', type
                FROM editorial.course_version_questions WHERE id = @question;
                INSERT INTO editorial.course_version_answer_options (id, correctness, position, question_id, text)
                SELECT gen_random_uuid(), correctness, position, @landed, text
                FROM editorial.course_version_answer_options WHERE question_id = @question;
                UPDATE editorial.course_versions SET title = title WHERE id = @id;
                """,
                connection,
                transaction))
            {
                landing.Parameters.AddWithValue("landed", landed);
                landing.Parameters.AddWithValue("question", question);
                landing.Parameters.AddWithValue("id", id);
                await landing.ExecuteNonQueryAsync(cancellationToken);
            }

            await gate.CommitAsync(cancellationToken);
        }

        using var response = await change;
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        response.StatusCode.ShouldNotBe(HttpStatusCode.InternalServerError, $"POST {added} answered a server error: {body}");

        // It loaded the version before the other question landed, so its row version is the one before: under one
        // snapshot (REPEATABLE READ) it saw none of the other question, and saving against the old row version loses.
        // The snapshot is taken at the version's own statement, before the gate commits, so it always loses (#170).
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict, $"POST {added} saved over a version it read before the other question landed: {body}");
        (await CodeOfAsync(response, cancellationToken)).ShouldBe("editorial.course-version-changed", body);
        var version = await ReadAsync(client, id, cancellationToken);
        var questions = version.Questions ?? [];
        version.Chapters.Count.ShouldBe(1, "the refused addition left a chapter behind");
        questions.Select(read => read.Id).ShouldBe([question, landed], "the refused addition changed the version's questions");

        questions.ShouldAllBe(read => read.Options.Count == 2, "a question lost or gained options in the race");
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
        await using (var gate = await EditorialDatabase.LockVersionAsync(connectionString, id, cancellationToken))
        {
            if (removalFirst)
            {
                removed = removal();
                await EditorialDatabase.WaitUntilBlockedAsync(connectionString, gate, 1, cancellationToken);
                changed = other();
            }
            else
            {
                changed = other();
                await EditorialDatabase.WaitUntilBlockedAsync(connectionString, gate, 1, cancellationToken);
                removed = removal();
            }

            await EditorialDatabase.WaitUntilBlockedAsync(connectionString, gate, 2, cancellationToken);
            await gate.RollbackAsync(cancellationToken);
        }

        var responses = await Task.WhenAll(removed, changed);
        return (responses[0], responses[1]);
    }

    /// <summary>
    /// Whether the removal won (true) or the other request did (false), when exactly one succeeded and the other was
    /// told the version changed or, for the other request, one of the codes it may also answer; null otherwise.
    /// </summary>
    private static Task<bool?> OutcomeAsync(
        HttpResponseMessage removal, HttpResponseMessage other, HttpStatusCode removed, HttpStatusCode succeeded, string[] otherMayAnswer, CancellationToken cancellationToken) =>
        OutcomeAsync(removal, other, removed, succeeded, otherMayAnswer, LostEitherWay, cancellationToken);

    /// <summary>As above, the loser refused with one of the codes in <paramref name="lostWith"/>.</summary>
    private static async Task<bool?> OutcomeAsync(
        HttpResponseMessage removal, HttpResponseMessage other, HttpStatusCode removed, HttpStatusCode succeeded, string[] otherMayAnswer, string[] lostWith, CancellationToken cancellationToken)
    {
        if (removal.StatusCode == removed && await IsChangedAsync(other, otherMayAnswer, lostWith, cancellationToken))
        {
            return true;
        }

        if (other.StatusCode == succeeded && await IsChangedAsync(removal, [], lostWith, cancellationToken))
        {
            return false;
        }

        return null;
    }

    private static async Task<bool> IsChangedAsync(HttpResponseMessage response, string[] mayAnswer, string[] lostWith, CancellationToken cancellationToken)
    {
        if (response.StatusCode is not (HttpStatusCode.Conflict or HttpStatusCode.NotFound))
        {
            return false;
        }

        var code = await CodeOfAsync(response, cancellationToken);
        return response.StatusCode == HttpStatusCode.Conflict
            ? lostWith.Contains(code)
            : mayAnswer.Contains(code);
    }

    private static async Task<string> CodesAsync(HttpResponseMessage first, HttpResponseMessage second, CancellationToken cancellationToken) =>
        $"bodies: {await first.Content.ReadAsStringAsync(cancellationToken)} | {await second.Content.ReadAsStringAsync(cancellationToken)}";

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

    private sealed record QuestionView(Guid Id, Guid ChapterId, string Type, string Text, IReadOnlyList<OptionView> Options);

    private sealed record OptionView(string Text);

    private sealed record Problem(string Code);
}
