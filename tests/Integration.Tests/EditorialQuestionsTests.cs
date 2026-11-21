using System.Buffers.Text;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// An editor maintains the questions of a draft through the edge and signs them off: each change is read back in
/// order with where the question came from and who approved it when (story E7); a question whose answers break its
/// type is kept as a draft but cannot be approved; a published version refuses every question change.
/// </summary>
public class EditorialQuestionsTests(AspireFixture aspire)
{
    private static readonly Uri Courses = new("/editorial/courses", UriKind.Relative);

    [Fact]
    public async Task Questions_are_added_and_read_back_in_their_order_as_written_by_a_person_and_unreviewed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        var chapter = await AddChapterAsync(client, id, "Händehygiene", cancellationToken);

        using var addedSingle = await client.PostAsJsonAsync(Questions(id), SingleChoice(chapter), cancellationToken);
        addedSingle.StatusCode.ShouldBe(HttpStatusCode.Created);
        addedSingle.Headers.Location.ShouldBe(new Uri($"/editorial/course-versions/{id}", UriKind.Relative));
        var single = await addedSingle.Content.ReadFromJsonAsync<Guid>(cancellationToken);
        var multiple = await AddQuestionAsync(client, id, MultipleChoice(chapter), cancellationToken);

        var questions = await QuestionsOfAsync(client, id, cancellationToken);

        questions.Select(question => (question.Id, question.Position, question.ChapterId)).ShouldBe([(single, 1, chapter), (multiple, 2, chapter)]);
        questions[0].Text.ShouldBe("Wann desinfizieren Sie die Hände?");
        questions[0].Type.ShouldBe("SingleChoice");
        questions[0].Options.Select(option => (option.Text, option.Correct)).ShouldBe([("Vor der Rezeptur", true), ("Einmal am Tag", false)]);
        questions[1].Text.ShouldBe("Welche Flächen werden desinfiziert?");
        questions[1].Type.ShouldBe("MultipleChoice");
        questions[1].Options.Select(option => (option.Text, option.Correct)).ShouldBe([("Rezepturtisch", true), ("Waage", true), ("Schaufenster", false)]);
        questions.SelectMany(question => question.Options).ShouldAllBe(option => option.Id != Guid.Empty);
        foreach (var question in questions)
        {
            question.Provenance.ShouldBe("Human");
            question.Review.ShouldBe("Unreviewed");
            question.ReviewedBy.ShouldBeNull();
            question.ReviewedAt.ShouldBeNull();
        }
    }

    [Fact]
    public async Task An_approved_question_is_read_back_with_the_editor_who_approved_it_and_when()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var editor = await SubjectOfAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        var chapter = await AddChapterAsync(client, id, "Händehygiene", cancellationToken);
        var question = await AddQuestionAsync(client, id, SingleChoice(chapter), cancellationToken);
        var before = TimeProvider.System.GetUtcNow();

        using var approved = await client.PostAsync(Approve(id, question), null, cancellationToken);

        var after = TimeProvider.System.GetUtcNow();
        approved.StatusCode.ShouldBe(HttpStatusCode.OK);
        var read = (await QuestionsOfAsync(client, id, cancellationToken)).ShouldHaveSingleItem();
        read.Review.ShouldBe("Approved");
        read.ReviewedBy.ShouldBe(editor);
        read.ReviewedAt.ShouldNotBeNull();
        read.ReviewedAt.Value.ShouldBeInRange(before.AddSeconds(-5), after.AddSeconds(5));
        read.Provenance.ShouldBe("Human");
    }

    [Fact]
    public async Task An_approved_question_that_is_edited_is_read_back_with_its_new_content_and_needs_a_review_again()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        var chapter = await AddChapterAsync(client, id, "Händehygiene", cancellationToken);
        var question = await AddQuestionAsync(client, id, SingleChoice(chapter), cancellationToken);
        await ApproveAsync(client, id, question, cancellationToken);

        using var edited = await client.PutAsJsonAsync(Question(id, question), MultipleChoice(chapter), cancellationToken);

        edited.StatusCode.ShouldBe(HttpStatusCode.OK);
        var read = (await QuestionsOfAsync(client, id, cancellationToken)).ShouldHaveSingleItem();
        read.Id.ShouldBe(question);
        read.Text.ShouldBe("Welche Flächen werden desinfiziert?");
        read.Type.ShouldBe("MultipleChoice");
        read.Options.Select(option => (option.Text, option.Correct)).ShouldBe([("Rezepturtisch", true), ("Waage", true), ("Schaufenster", false)]);
        read.Review.ShouldBe("Unreviewed");
        read.ReviewedBy.ShouldBeNull();
        read.ReviewedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Changing_a_chapter_sends_its_questions_back_to_review_and_removing_it_removes_them()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        var changed = await AddChapterAsync(client, id, "Händehygiene", cancellationToken);
        var kept = await AddChapterAsync(client, id, "Flächendesinfektion", cancellationToken);
        var aboutChanged = await AddQuestionAsync(client, id, SingleChoice(changed), cancellationToken);
        var aboutKept = await AddQuestionAsync(client, id, MultipleChoice(kept), cancellationToken);
        await ApproveAsync(client, id, aboutChanged, cancellationToken);
        await ApproveAsync(client, id, aboutKept, cancellationToken);

        using var replaced = await client.PutAsJsonAsync(Chapter(id, changed), new { title = "Händehygiene, überarbeitet", text = "Neu." }, cancellationToken);

        replaced.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterChange = await QuestionsOfAsync(client, id, cancellationToken);
        var sentBack = afterChange.Single(question => question.Id == aboutChanged);
        sentBack.Review.ShouldBe("Unreviewed");
        sentBack.ReviewedBy.ShouldBeNull();
        sentBack.ReviewedAt.ShouldBeNull();
        var untouched = afterChange.Single(question => question.Id == aboutKept);
        untouched.Review.ShouldBe("Approved");
        untouched.ReviewedBy.ShouldNotBeNull();

        using var removed = await client.DeleteAsync(Chapter(id, changed), cancellationToken);

        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        var afterRemoval = await QuestionsOfAsync(client, id, cancellationToken);
        afterRemoval.Select(question => (question.Id, question.Position)).ShouldBe([(aboutKept, 1)]);
    }

    [Fact]
    public async Task A_question_whose_answers_break_the_rules_of_its_type_is_kept_as_a_draft_but_cannot_be_approved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        var chapter = await AddChapterAsync(client, id, "Händehygiene", cancellationToken);
        var twoCorrect = await AddQuestionAsync(
            client, id, Body(chapter, "Wann desinfizieren Sie die Hände?", "SingleChoice", ("Vor der Rezeptur", true), ("Nach der Rezeptur", true)), cancellationToken);
        var noneCorrect = await AddQuestionAsync(
            client, id, Body(chapter, "Welche Flächen werden desinfiziert?", "MultipleChoice", ("Schaufenster", false), ("Parkplatz", false)), cancellationToken);
        var oneOption = await AddQuestionAsync(
            client, id, Body(chapter, "Was gehört zur Händehygiene?", "SingleChoice", ("Desinfizieren", true)), cancellationToken);

        using var refusedSingle = await client.PostAsync(Approve(id, twoCorrect), null, cancellationToken);
        using var refusedMultiple = await client.PostAsync(Approve(id, noneCorrect), null, cancellationToken);
        using var refusedOne = await client.PostAsync(Approve(id, oneOption), null, cancellationToken);

        refusedSingle.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeOfAsync(refusedSingle, cancellationToken)).ShouldBe("editorial.single-choice-one-correct");
        refusedMultiple.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeOfAsync(refusedMultiple, cancellationToken)).ShouldBe("editorial.multiple-choice-a-correct");
        refusedOne.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await CodeOfAsync(refusedOne, cancellationToken)).ShouldBe("editorial.too-few-options");
        var questions = await QuestionsOfAsync(client, id, cancellationToken);
        questions.Select(question => question.Id).ShouldBe([twoCorrect, noneCorrect, oneOption]);
        questions.ShouldAllBe(question => question.Review == "Unreviewed" && question.ReviewedBy == null);
    }

    [Fact]
    public async Task A_removed_question_is_no_longer_read_back_and_cannot_be_removed_twice()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        var chapter = await AddChapterAsync(client, id, "Händehygiene", cancellationToken);
        var first = await AddQuestionAsync(client, id, SingleChoice(chapter), cancellationToken);
        var second = await AddQuestionAsync(client, id, MultipleChoice(chapter), cancellationToken);

        using var removed = await client.DeleteAsync(Question(id, first), cancellationToken);
        using var removedAgain = await client.DeleteAsync(Question(id, first), cancellationToken);

        removed.StatusCode.ShouldBe(HttpStatusCode.OK);
        removedAgain.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(removedAgain, cancellationToken)).ShouldBe("editorial.question-not-found");
        (await QuestionsOfAsync(client, id, cancellationToken)).Select(question => (question.Id, question.Position)).ShouldBe([(second, 1)]);
    }

    [Theory]
    [InlineData("{'chapterId':'{chapter}','text':'  ','type':'SingleChoice','options':{options}}", "editorial.invalid-question-text")]
    [InlineData("{'chapterId':'{chapter}','type':'SingleChoice','options':{options}}", "editorial.invalid-question-text")]
    [InlineData("{'chapterId':'{chapter}','text':'Wann\\u0000 desinfizieren?','type':'SingleChoice','options':{options}}", "editorial.invalid-question-text")]
    [InlineData("{'chapterId':'{chapter}','text':'{tooLong}','type':'SingleChoice','options':{options}}", "editorial.invalid-question-text")]
    [InlineData("{'chapterId':'{chapter}','text':'Wann?','options':{options}}", "editorial.invalid-question-type")]
    [InlineData("{'chapterId':'{chapter}','text':'Wann?','type':7,'options':{options}}", "editorial.invalid-question-type")]
    [InlineData("{'chapterId':'{chapter}','text':'Wann?','type':'TrueFalse','options':{options}}", null)]
    [InlineData("{'chapterId':'{chapter}','text':'Wann?','type':'SingleChoice'}", "editorial.invalid-answer-options")]
    [InlineData("{'chapterId':'{chapter}','text':'Wann?','type':'SingleChoice','options':null}", "editorial.invalid-answer-options")]
    [InlineData("{'chapterId':'{chapter}','text':'Wann?','type':'SingleChoice','options':[{'text':'Vor','correct':true},null]}", "editorial.invalid-answer-options")]
    [InlineData("{'chapterId':'{chapter}','text':'Wann?','type':'SingleChoice','options':[{'text':'Vor','correct':true},{'text':' ','correct':false}]}", "editorial.invalid-answer-text")]
    [InlineData("{'chapterId':'{chapter}','text':'Wann?','type':'SingleChoice','options':[{'text':'Vor','correct':true},{'correct':false}]}", "editorial.invalid-answer-text")]
    [InlineData("{'chapterId':'{chapter}','text':'Wann?','type':'SingleChoice','options':[{'text':'Vor','correct':true},{'text':'Na\\u0000ch','correct':false}]}", "editorial.invalid-answer-text")]
    public async Task A_question_the_caller_got_wrong_is_refused_as_the_callers_mistake_whether_added_or_edited(string template, string? code)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        var chapter = await AddChapterAsync(client, id, "Händehygiene", cancellationToken);
        var question = await AddQuestionAsync(client, id, SingleChoice(chapter), cancellationToken);
        var before = await QuestionsOfAsync(client, id, cancellationToken);
        var body = template
            .Replace("{options}", "[{'text':'Vor','correct':true},{'text':'Nach','correct':false}]", StringComparison.Ordinal)
            .Replace("{chapter}", chapter.ToString(), StringComparison.Ordinal)
            .Replace("{tooLong}", new string('a', 1001), StringComparison.Ordinal)
            .Replace('\'', '"');

        using var added = await client.PostAsync(Questions(id), Json(body), cancellationToken);
        using var edited = await client.PutAsync(Question(id, question), Json(body), cancellationToken);

        foreach (var refused in new[] { added, edited })
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, $"{refused.RequestMessage?.Method} {body}");
            if (code is not null)
            {
                (await CodeOfAsync(refused, cancellationToken)).ShouldBe(code);
            }
        }

        var after = await QuestionsOfAsync(client, id, cancellationToken);
        after.Select(read => (read.Id, read.Text, read.Type)).ShouldBe(before.Select(read => (read.Id, read.Text, read.Type)));
    }

    [Fact]
    public async Task An_unknown_or_empty_version_question_or_chapter_is_not_found()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        var chapter = await AddChapterAsync(client, id, "Händehygiene", cancellationToken);
        var question = await AddQuestionAsync(client, id, SingleChoice(chapter), cancellationToken);
        var stranger = Guid.CreateVersion7();

        List<HttpResponseMessage> versionNotFound =
        [
            await client.PostAsJsonAsync(Questions(stranger), SingleChoice(chapter), cancellationToken),
            await client.PutAsJsonAsync(Question(stranger, question), SingleChoice(chapter), cancellationToken),
            await client.DeleteAsync(Question(stranger, question), cancellationToken),
            await client.PostAsync(Approve(stranger, question), null, cancellationToken),
            await client.PostAsJsonAsync(Questions(Guid.Empty), SingleChoice(chapter), cancellationToken),
            await client.PutAsJsonAsync(Question(Guid.Empty, question), SingleChoice(chapter), cancellationToken),
            await client.DeleteAsync(Question(Guid.Empty, question), cancellationToken),
            await client.PostAsync(Approve(Guid.Empty, question), null, cancellationToken),
        ];
        List<HttpResponseMessage> questionNotFound =
        [
            await client.PutAsJsonAsync(Question(id, stranger), SingleChoice(chapter), cancellationToken),
            await client.DeleteAsync(Question(id, stranger), cancellationToken),
            await client.PostAsync(Approve(id, stranger), null, cancellationToken),
            await client.PutAsJsonAsync(Question(id, Guid.Empty), SingleChoice(chapter), cancellationToken),
            await client.DeleteAsync(Question(id, Guid.Empty), cancellationToken),
            await client.PostAsync(Approve(id, Guid.Empty), null, cancellationToken),
        ];
        List<HttpResponseMessage> chapterNotFound =
        [
            await client.PostAsJsonAsync(Questions(id), SingleChoice(stranger), cancellationToken),
            await client.PostAsJsonAsync(Questions(id), SingleChoice(Guid.Empty), cancellationToken),
            await client.PutAsJsonAsync(Question(id, question), SingleChoice(Guid.Empty), cancellationToken),
            await client.PostAsync(Questions(id), Json("{\"text\":\"Wann?\",\"type\":\"SingleChoice\",\"options\":[]}"), cancellationToken),
        ];

        try
        {
            await ShouldAllBeNotFoundAsync(versionNotFound, "editorial.course-version-not-found", cancellationToken);
            await ShouldAllBeNotFoundAsync(questionNotFound, "editorial.question-not-found", cancellationToken);
            await ShouldAllBeNotFoundAsync(chapterNotFound, "editorial.chapter-not-found", cancellationToken);
            (await QuestionsOfAsync(client, id, cancellationToken)).Select(read => read.Id).ShouldBe([question]);
        }
        finally
        {
            foreach (var response in versionNotFound.Concat(questionNotFound).Concat(chapterNotFound))
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task A_published_version_refuses_every_question_change_and_stays_as_it_was()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        var chapter = await AddChapterAsync(client, id, "Händehygiene", cancellationToken);
        var question = await AddQuestionAsync(client, id, SingleChoice(chapter), cancellationToken);
        await EditorialDatabase.SeedPublishedAsync(await EditorialDatabase.ConnectionStringAsync(aspire, cancellationToken), id, cancellationToken);
        var before = (await QuestionsOfAsync(client, id, cancellationToken)).ShouldHaveSingleItem();

        using var added = await client.PostAsJsonAsync(Questions(id), MultipleChoice(chapter), cancellationToken);
        using var edited = await client.PutAsJsonAsync(Question(id, question), MultipleChoice(chapter), cancellationToken);
        using var approved = await client.PostAsync(Approve(id, question), null, cancellationToken);
        using var removed = await client.DeleteAsync(Question(id, question), cancellationToken);

        foreach (var refused in new[] { added, edited, approved, removed })
        {
            refused.StatusCode.ShouldBe(HttpStatusCode.Conflict, $"{refused.RequestMessage?.Method} {refused.RequestMessage?.RequestUri}");
            (await CodeOfAsync(refused, cancellationToken)).ShouldBe("editorial.not-a-draft");
        }

        var after = (await QuestionsOfAsync(client, id, cancellationToken)).ShouldHaveSingleItem();
        after.Id.ShouldBe(before.Id);
        after.Text.ShouldBe(before.Text);
        after.Type.ShouldBe(before.Type);
        after.Options.ShouldBe(before.Options);
        after.Review.ShouldBe("Unreviewed");
        after.ReviewedBy.ShouldBeNull();
    }

    [Fact]
    public async Task Of_two_concurrent_question_additions_to_one_version_one_is_created_and_the_other_is_told_the_version_changed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        var chapter = await AddChapterAsync(client, id, "Händehygiene", cancellationToken);
        var existing = await AddQuestionAsync(client, id, SingleChoice(chapter), cancellationToken);
        var connectionString = await EditorialDatabase.ConnectionStringAsync(aspire, cancellationToken);

        HttpResponseMessage[] responses;
        await using (var gate = await EditorialDatabase.LockVersionAsync(connectionString, id, cancellationToken))
        {
            var first = client.PostAsJsonAsync(Questions(id), MultipleChoice(chapter), cancellationToken);
            var second = client.PostAsJsonAsync(Questions(id), SingleChoice(chapter), cancellationToken);
            await EditorialDatabase.WaitUntilBlockedAsync(connectionString, 2, cancellationToken);
            await gate.CommitAsync(cancellationToken);
            responses = await Task.WhenAll(first, second);
        }

        try
        {
            responses.Select(response => response.StatusCode).ShouldBe([HttpStatusCode.Created, HttpStatusCode.Conflict], ignoreOrder: true);
            var lost = responses.Single(response => response.StatusCode == HttpStatusCode.Conflict);
            (await CodeOfAsync(lost, cancellationToken)).ShouldBe("editorial.course-version-changed");
            var won = responses.Single(response => response.StatusCode == HttpStatusCode.Created);
            var added = await won.Content.ReadFromJsonAsync<Guid>(cancellationToken);
            var questions = await QuestionsOfAsync(client, id, cancellationToken);
            questions.Select(question => (question.Id, question.Position)).ShouldBe([(existing, 1), (added, 2)]);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    private static Uri Questions(Guid id) => new($"/editorial/course-versions/{id}/questions", UriKind.Relative);

    private static Uri Question(Guid id, Guid question) => new($"/editorial/course-versions/{id}/questions/{question}", UriKind.Relative);

    private static Uri Approve(Guid id, Guid question) => new($"/editorial/course-versions/{id}/questions/{question}/approve", UriKind.Relative);

    private static Uri Chapter(Guid id, Guid chapter) => new($"/editorial/course-versions/{id}/chapters/{chapter}", UriKind.Relative);

    private static object SingleChoice(Guid chapter) =>
        Body(chapter, "Wann desinfizieren Sie die Hände?", "SingleChoice", ("Vor der Rezeptur", true), ("Einmal am Tag", false));

    private static object MultipleChoice(Guid chapter) =>
        Body(chapter, "Welche Flächen werden desinfiziert?", "MultipleChoice", ("Rezepturtisch", true), ("Waage", true), ("Schaufenster", false));

    private static object Body(Guid chapter, string text, string type, params (string Text, bool Correct)[] options) =>
        new { chapterId = chapter, text, type, options = options.Select(option => new { text = option.Text, correct = option.Correct }).ToList() };

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static async Task<Guid> CreateAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var created = await client.PostAsJsonAsync(Courses, new { title = $"Hygiene {Guid.CreateVersion7()}" }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private static async Task<Guid> AddChapterAsync(HttpClient client, Guid id, string title, CancellationToken cancellationToken)
    {
        using var added = await client.PostAsJsonAsync(new Uri($"/editorial/course-versions/{id}/chapters", UriKind.Relative), new { title, text = "Vor und nach …" }, cancellationToken);
        added.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await added.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private static async Task<Guid> AddQuestionAsync(HttpClient client, Guid id, object question, CancellationToken cancellationToken)
    {
        using var added = await client.PostAsJsonAsync(Questions(id), question, cancellationToken);
        added.StatusCode.ShouldBe(HttpStatusCode.Created, $"POST {Questions(id)} answered {(int)added.StatusCode}.");
        return await added.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private static async Task ApproveAsync(HttpClient client, Guid id, Guid question, CancellationToken cancellationToken)
    {
        using var approved = await client.PostAsync(Approve(id, question), null, cancellationToken);
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, $"POST {Approve(id, question)} answered {(int)approved.StatusCode}.");
    }

    private static async Task<IReadOnlyList<QuestionView>> QuestionsOfAsync(HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri($"/editorial/course-versions/{id}", UriKind.Relative), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var version = await response.Content.ReadFromJsonAsync<VersionView>(cancellationToken);
        version.ShouldNotBeNull();
        version.Questions.ShouldNotBeNull("GET answered the version without its questions.");
        return version.Questions;
    }

    private static async Task ShouldAllBeNotFoundAsync(IEnumerable<HttpResponseMessage> responses, string code, CancellationToken cancellationToken)
    {
        foreach (var response in responses)
        {
            var request = $"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri}";
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, request);
            (await CodeOfAsync(response, cancellationToken)).ShouldBe(code, request);
        }
    }

    private static async Task<string> CodeOfAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var problem = await response.Content.ReadFromJsonAsync<Problem>(cancellationToken);
        problem.ShouldNotBeNull();
        return problem.Code;
    }

    /// <summary>The Keycloak subject of a local user, read from the access token the realm issues to it.</summary>
    private async Task<Guid> SubjectOfAsync(string user)
    {
        var token = await aspire.RequestAccessTokenAsync(user);
        using var payload = JsonDocument.Parse(Encoding.UTF8.GetString(Base64Url.DecodeFromChars(token.Split('.')[1])));
        return Guid.Parse(payload.RootElement.GetProperty("sub").GetString() ?? string.Empty, System.Globalization.CultureInfo.InvariantCulture);
    }

    private sealed record VersionView(Guid Id, string Status, IReadOnlyList<QuestionView>? Questions);

    private sealed record QuestionView(
        Guid Id, int Position, Guid ChapterId, string Text, string Type, IReadOnlyList<OptionView> Options, string Provenance, string Review, Guid? ReviewedBy, DateTimeOffset? ReviewedAt);

    private sealed record OptionView(Guid Id, string Text, bool Correct);

    private sealed record Problem(string Code);
}
