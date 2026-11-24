using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// An editor sets the test blueprint of a draft through the edge (story E3): how many questions, the passing score
/// and how long a certificate is valid. A version is read back with its blueprint; a changed setting takes every
/// question's approval away (#199); what a caller gets wrong is refused with its own code and never reaches 500; a
/// version that is no draft keeps its blueprint.
/// </summary>
[Collection(EditorialDrafts.Name)]
public class EditorialBlueprintTests(AspireFixture aspire)
{
    private static readonly Uri Courses = new("/editorial/courses", UriKind.Relative);
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task A_new_course_version_is_read_with_ten_questions_80_percent_and_12_months()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);

        var version = await ReadAsync(client, id, cancellationToken);

        version.Blueprint.ShouldBe(new BlueprintView(10, 80, 12));
    }

    [Fact]
    public async Task An_editor_sets_a_drafts_blueprint_and_reads_it_back()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);

        using var set = await client.PutAsJsonAsync(Blueprint(id), new { questions = 12, passingScorePercent = 75, validityMonths = 24 }, cancellationToken);

        set.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(client, id, cancellationToken)).Blueprint.ShouldBe(new BlueprintView(12, 75, 24));
    }

    [Theory]
    [InlineData(11, 80, 12)]
    [InlineData(10, 81, 12)]
    [InlineData(10, 80, 13)]
    public async Task Changing_one_setting_takes_every_questions_approval_away(int questions, int passingScore, int validityMonths)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        var chapter = await AddChapterAsync(client, id, cancellationToken);
        var first = await AddQuestionAsync(client, id, chapter, cancellationToken);
        var second = await AddQuestionAsync(client, id, chapter, cancellationToken);
        await ApproveAsync(client, id, first, cancellationToken);
        await ApproveAsync(client, id, second, cancellationToken);

        using var set = await client.PutAsJsonAsync(Blueprint(id), new { questions, passingScorePercent = passingScore, validityMonths }, cancellationToken);

        set.StatusCode.ShouldBe(HttpStatusCode.OK);
        var version = await ReadAsync(client, id, cancellationToken);
        version.Blueprint.ShouldBe(new BlueprintView(questions, passingScore, validityMonths));
        version.Questions.ShouldNotBeNull();
        version.Questions.Select(read => read.Id).ShouldBe([first, second], ignoreOrder: true);
        version.Questions.ShouldAllBe(read => read.Review == "Unreviewed" && read.ReviewedBy == null && read.ReviewedAt == null);
    }

    [Fact]
    public async Task Saving_the_same_settings_again_keeps_every_approval()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        var chapter = await AddChapterAsync(client, id, cancellationToken);
        var first = await AddQuestionAsync(client, id, chapter, cancellationToken);
        var second = await AddQuestionAsync(client, id, chapter, cancellationToken);
        await ApproveAsync(client, id, first, cancellationToken);
        await ApproveAsync(client, id, second, cancellationToken);
        var before = await ReadAsync(client, id, cancellationToken);
        before.Questions.ShouldNotBeNull();
        before.Questions.ShouldAllBe(read => read.Review == "Approved" && read.ReviewedBy != null && read.ReviewedAt != null);

        using var set = await client.PutAsJsonAsync(Blueprint(id), new { questions = 10, passingScorePercent = 80, validityMonths = 12 }, cancellationToken);

        set.StatusCode.ShouldBe(HttpStatusCode.OK);
        var version = await ReadAsync(client, id, cancellationToken);
        version.Blueprint.ShouldBe(new BlueprintView(10, 80, 12));
        version.Questions.ShouldNotBeNull();
        version.Questions.OrderBy(read => read.Id).ShouldBe(before.Questions.OrderBy(read => read.Id));
    }

    [Fact]
    public async Task Numbers_sent_as_strings_are_read_as_numbers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);

        using var set = await client.PutAsync(Blueprint(id), Json("""{"questions":"5","passingScorePercent":"70","validityMonths":"24"}"""), cancellationToken);

        set.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await ReadAsync(client, id, cancellationToken)).Blueprint.ShouldBe(new BlueprintView(5, 70, 24));
    }

    [Theory]
    [InlineData("{'questions':0,'passingScorePercent':80,'validityMonths':12}", "editorial.invalid-question-count")]
    [InlineData("{'questions':-1,'passingScorePercent':80,'validityMonths':12}", "editorial.invalid-question-count")]
    [InlineData("{'questions':51,'passingScorePercent':80,'validityMonths':12}", "editorial.invalid-question-count")]
    [InlineData("{'passingScorePercent':80,'validityMonths':12}", "editorial.invalid-question-count")]
    [InlineData("{'questions':null,'passingScorePercent':80,'validityMonths':12}", "editorial.invalid-question-count")]
    [InlineData("{'questions':10,'passingScorePercent':-1,'validityMonths':12}", "editorial.invalid-passing-score")]
    [InlineData("{'questions':10,'passingScorePercent':101,'validityMonths':12}", "editorial.invalid-passing-score")]
    [InlineData("{'questions':10,'validityMonths':12}", "editorial.invalid-passing-score")]
    [InlineData("{'questions':10,'passingScorePercent':null,'validityMonths':12}", "editorial.invalid-passing-score")]
    [InlineData("{'questions':10,'passingScorePercent':80,'validityMonths':0}", "editorial.invalid-validity")]
    [InlineData("{'questions':10,'passingScorePercent':80,'validityMonths':-1}", "editorial.invalid-validity")]
    [InlineData("{'questions':10,'passingScorePercent':80,'validityMonths':61}", "editorial.invalid-validity")]
    [InlineData("{'questions':10,'passingScorePercent':80}", "editorial.invalid-validity")]
    [InlineData("{'questions':10,'passingScorePercent':80,'validityMonths':null}", "editorial.invalid-validity")]
    [InlineData("{'questions':0,'passingScorePercent':101,'validityMonths':12}", "editorial.invalid-passing-score")]
    [InlineData("{}", "editorial.invalid-question-count")]
    public async Task A_blueprint_value_out_of_range_is_refused_as_the_callers_mistake(string body, string code)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        var json = body.Replace('\'', '"');

        using var refused = await client.PutAsync(Blueprint(id), Json(json), cancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, json);
        (await CodeOfAsync(refused, cancellationToken)).ShouldBe(code, json);
        (await ReadAsync(client, id, cancellationToken)).Blueprint.ShouldBe(new BlueprintView(10, 80, 12));
    }

    /// <summary>
    /// A count below one is refused by the domain's blueprint and one that is no count at all by the request's
    /// validation; either way the caller reads the one English message its code has (docs/adr, #145).
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task A_question_count_of_0_or_minus_1_answers_the_codes_english_message(int questions)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);

        using var refused = await client.PutAsJsonAsync(Blueprint(id), new { questions, passingScorePercent = 80, validityMonths = 12 }, cancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        using var problem = JsonDocument.Parse(await refused.Content.ReadAsStringAsync(cancellationToken));
        problem.RootElement.GetProperty("code").GetString().ShouldBe("editorial.invalid-question-count");
        problem.RootElement.GetProperty("title").GetString().ShouldBe(ErrorCodeCatalog.MessageOf("editorial.invalid-question-count"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{'questions':10.5,'passingScorePercent':80,'validityMonths':12}")]
    [InlineData("{'questions':true,'passingScorePercent':80,'validityMonths':12}")]
    [InlineData("{'questions':{},'passingScorePercent':80,'validityMonths':12}")]
    [InlineData("{'questions':10,'passingScorePercent':80.5,'validityMonths':12}")]
    [InlineData("{'questions':3000000000,'passingScorePercent':80,'validityMonths':12}")]
    public async Task A_body_that_cannot_be_read_is_a_bad_request_never_a_server_error(string? body)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Put, Blueprint(id)) { Content = body is null ? null : Json(body.Replace('\'', '"')) };

        using var refused = await client.SendAsync(request, cancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.BadRequest, body ?? "no body");
    }

    [Fact]
    public async Task An_unknown_or_empty_version_is_not_found()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var body = new { questions = 12, passingScorePercent = 75, validityMonths = 24 };

        using var unknown = await client.PutAsJsonAsync(Blueprint(Guid.CreateVersion7()), body, cancellationToken);
        using var empty = await client.PutAsJsonAsync(Blueprint(Guid.Empty), body, cancellationToken);

        foreach (var response in new[] { unknown, empty })
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NotFound, $"{response.RequestMessage?.RequestUri}");
            (await CodeOfAsync(response, cancellationToken)).ShouldBe("editorial.course-version-not-found");
        }
    }

    [Fact]
    public async Task A_published_version_refuses_a_blueprint_and_keeps_its_own()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("editor");
        var id = await CreateAsync(client, cancellationToken);
        await EditorialDatabase.SeedPublishedAsync(await EditorialDatabase.ConnectionStringAsync(aspire, cancellationToken), id, cancellationToken);

        using var refused = await client.PutAsJsonAsync(Blueprint(id), new { questions = 12, passingScorePercent = 75, validityMonths = 24 }, cancellationToken);

        refused.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await CodeOfAsync(refused, cancellationToken)).ShouldBe("editorial.not-a-draft");
        (await ReadAsync(client, id, cancellationToken)).Blueprint.ShouldBe(new BlueprintView(10, 80, 12));
    }

    private static Uri Blueprint(Guid id) => new($"/editorial/course-versions/{id}/blueprint", UriKind.Relative);

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static async Task<Guid> CreateAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var created = await client.PostAsJsonAsync(Courses, new { title = $"Hygiene {Guid.CreateVersion7()}" }, cancellationToken);
        created.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private static async Task<VersionView> ReadAsync(HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(new Uri($"/editorial/course-versions/{id}", UriKind.Relative), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var version = await response.Content.ReadFromJsonAsync<VersionView>(cancellationToken);
        version.ShouldNotBeNull();
        return version;
    }

    private static async Task<Guid> AddChapterAsync(HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        using var added = await client.PostAsJsonAsync(new Uri($"/editorial/course-versions/{id}/chapters", UriKind.Relative), new { title = "Händehygiene", text = "Vor und nach …" }, cancellationToken);
        added.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await added.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private static async Task<Guid> AddQuestionAsync(HttpClient client, Guid id, Guid chapter, CancellationToken cancellationToken)
    {
        var question = new
        {
            chapterId = chapter,
            text = "Wann desinfizieren Sie die Hände?",
            type = "SingleChoice",
            options = new[] { new { text = "Vor der Rezeptur", correct = true }, new { text = "Einmal am Tag", correct = false } },
        };
        using var added = await client.PostAsJsonAsync(new Uri($"/editorial/course-versions/{id}/questions", UriKind.Relative), question, cancellationToken);
        added.StatusCode.ShouldBe(HttpStatusCode.Created);
        return await added.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    private static async Task ApproveAsync(HttpClient client, Guid id, Guid question, CancellationToken cancellationToken)
    {
        var path = new Uri($"/editorial/course-versions/{id}/questions/{question}/approve", UriKind.Relative);
        using var approved = await client.PostAsync(path, null, cancellationToken);
        approved.StatusCode.ShouldBe(HttpStatusCode.OK, $"POST {path} answered {(int)approved.StatusCode}.");
    }

    private static async Task<string> CodeOfAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        body.ShouldNotBeNullOrWhiteSpace($"{response.RequestMessage?.Method} {response.RequestMessage?.RequestUri} answered {(int)response.StatusCode} without problem details.");
        var problem = JsonSerializer.Deserialize<Problem>(body, Web);
        problem.ShouldNotBeNull();
        return problem.Code;
    }

    private sealed record VersionView(Guid Id, string Status, BlueprintView? Blueprint, IReadOnlyList<QuestionView>? Questions);

    private sealed record QuestionView(Guid Id, string Review, Guid? ReviewedBy, DateTimeOffset? ReviewedAt);

    private sealed record BlueprintView(int Questions, int PassingScorePercent, int ValidityMonths);

    private sealed record Problem(string Code);
}
