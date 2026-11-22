using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// The editorial workflow through the edge, as an editor walks it: a course whose first version is ready to submit
/// (a blueprint of one question, a text chapter and a single-choice question), then submit, approve and publish.
/// Every step asserts that the edge accepted it, naming the request when it did not.
/// </summary>
internal static class EditorialWorkflow
{
    private static readonly Uri Courses = new("/editorial/courses", UriKind.Relative);

    /// <summary>A new course's first version: a draft with one text chapter and one unapproved question, as many as its blueprint asks.</summary>
    public static async Task<WorkflowDraft> ReadyToSubmitAsync(HttpClient client, CancellationToken cancellationToken)
    {
        Ensure.That(client).IsNotNull();

        Guid id;
        using (var created = await client.PostAsJsonAsync(Courses, new { title = $"Hygiene {Guid.CreateVersion7()}" }, cancellationToken))
        {
            created.StatusCode.ShouldBe(HttpStatusCode.Created, $"POST {Courses} answered {(int)created.StatusCode}.");
            id = await created.Content.ReadFromJsonAsync<Guid>(cancellationToken);
        }

        using (var set = await client.PutAsJsonAsync(VersionPath(id, "blueprint"), new { questions = 1, passingScorePercent = 80, validityMonths = 12 }, cancellationToken))
        {
            set.StatusCode.ShouldBe(HttpStatusCode.OK, $"PUT {VersionPath(id, "blueprint")} answered {(int)set.StatusCode}.");
        }

        var chapter = await AddChapterAsync(client, id, cancellationToken);
        Guid question;
        using (var added = await client.PostAsJsonAsync(VersionPath(id, "questions"), SingleChoice(chapter), cancellationToken))
        {
            added.StatusCode.ShouldBe(HttpStatusCode.Created, $"POST {VersionPath(id, "questions")} answered {(int)added.StatusCode}.");
            question = await added.Content.ReadFromJsonAsync<Guid>(cancellationToken);
        }

        var version = await ReadAsync(client, id, cancellationToken);
        return new WorkflowDraft(id, version.CourseId, chapter, question);
    }

    /// <summary>Adds a Markdown chapter to the version and answers its id.</summary>
    public static async Task<Guid> AddChapterAsync(HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        Ensure.That(client).IsNotNull();

        using var added = await client.PostAsJsonAsync(VersionPath(id, "chapters"), new { title = "Händehygiene", text = "Vor und nach …" }, cancellationToken);
        added.StatusCode.ShouldBe(HttpStatusCode.Created, $"POST {VersionPath(id, "chapters")} answered {(int)added.StatusCode}.");
        return await added.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    public static Task SubmitAsync(HttpClient client, Guid id, CancellationToken cancellationToken) =>
        ShouldBeAcceptedAsync(client, id, "submit", cancellationToken);

    public static Task PublishAsync(HttpClient client, Guid id, CancellationToken cancellationToken) =>
        ShouldBeAcceptedAsync(client, id, "publish", cancellationToken);

    /// <summary>Approves every question of the version, as the calling editor.</summary>
    public static async Task ApproveAllAsync(HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        Ensure.That(client).IsNotNull();

        foreach (var question in (await ReadAsync(client, id, cancellationToken)).Questions)
        {
            var approve = VersionPath(id, $"questions/{question.Id}/approve");
            using var approved = await client.PostAsync(approve, null, cancellationToken);
            approved.StatusCode.ShouldBe(HttpStatusCode.OK, $"POST {approve} answered {(int)approved.StatusCode}.");
        }
    }

    /// <summary>A new course whose first version was submitted, approved and published by the calling editor.</summary>
    public static async Task<WorkflowDraft> PublishedAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var draft = await ReadyToSubmitAsync(client, cancellationToken);
        await SubmitAsync(client, draft.Id, cancellationToken);
        await ApproveAllAsync(client, draft.Id, cancellationToken);
        await PublishAsync(client, draft.Id, cancellationToken);
        return draft;
    }

    /// <summary>Drafts the next version of a published one (<c>POST …/{id}/next</c>) and answers its id.</summary>
    public static async Task<Guid> DraftNextAsync(HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        Ensure.That(client).IsNotNull();

        using var drafted = await client.PostAsync(VersionPath(id, "next"), null, cancellationToken);
        drafted.StatusCode.ShouldBe(
            HttpStatusCode.Created,
            $"POST {VersionPath(id, "next")} answered {(int)drafted.StatusCode} {await drafted.Content.ReadAsStringAsync(cancellationToken)}");
        return await drafted.Content.ReadFromJsonAsync<Guid>(cancellationToken);
    }

    /// <summary>Sends one of the workflow's steps (submit, return, publish, retire) to the version, with no body.</summary>
    public static Task<HttpResponseMessage> StepAsync(HttpClient client, Guid id, string step, CancellationToken cancellationToken)
    {
        Ensure.That(client).IsNotNull();
        return client.PostAsync(VersionPath(id, step), null, cancellationToken);
    }

    public static async Task<WorkflowVersion> ReadAsync(HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        Ensure.That(client).IsNotNull();

        using var response = await client.GetAsync(new Uri($"/editorial/course-versions/{id}", UriKind.Relative), cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK, $"GET /editorial/course-versions/{id} answered {(int)response.StatusCode}.");
        var version = await response.Content.ReadFromJsonAsync<WorkflowVersion>(cancellationToken);
        version.ShouldNotBeNull();
        return version;
    }

    /// <summary>The version's chapters and questions as the edge answers them, to compare a version before and after a change.</summary>
    public static async Task<string> ContentOfAsync(HttpClient client, Guid id, CancellationToken cancellationToken)
    {
        Ensure.That(client).IsNotNull();

        var json = await client.GetStringAsync(new Uri($"/editorial/course-versions/{id}", UriKind.Relative), cancellationToken);
        using var document = JsonDocument.Parse(json);
        return $"{document.RootElement.GetProperty("chapters").GetRawText()}\n{document.RootElement.GetProperty("questions").GetRawText()}";
    }

    /// <summary>Whether the list of the versions of the given status holds the version, read a page at a time until the pages run out.</summary>
    public static async Task<bool> IsListedAsync(HttpClient client, string status, Guid id, CancellationToken cancellationToken)
    {
        Ensure.That(client).IsNotNull();

        for (var page = 1; ; page++)
        {
            var listed = await client.GetFromJsonAsync<WorkflowSummaryPage>(new Uri($"/editorial/course-versions?status={status}&page={page}&pageSize=100", UriKind.Relative), cancellationToken);
            listed.ShouldNotBeNull();
            listed.Items.ShouldAllBe(version => version.Status == status);
            if (listed.Items.Any(version => version.Id == id))
            {
                return true;
            }

            if (listed.Items.Count == 0 || page * listed.PageSize >= listed.TotalCount)
            {
                return false;
            }
        }
    }

    public static async Task<string> CodeOfAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        Ensure.That(response).IsNotNull();

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(body.Length == 0 ? "{}" : body);
        return document.RootElement.TryGetProperty("code", out var code) ? code.GetString() ?? string.Empty : $"(no code in \"{body}\")";
    }

    private static async Task ShouldBeAcceptedAsync(HttpClient client, Guid id, string step, CancellationToken cancellationToken)
    {
        using var response = await StepAsync(client, id, step, cancellationToken);
        response.StatusCode.ShouldBe(
            HttpStatusCode.OK,
            $"POST {VersionPath(id, step)} answered {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(cancellationToken)}");
    }

    private static Uri VersionPath(Guid id, string path) => new($"/editorial/course-versions/{id}/{path}", UriKind.Relative);

    private static object SingleChoice(Guid chapter) =>
        new
        {
            chapterId = chapter,
            text = "Wann desinfizieren Sie die Hände?",
            type = "SingleChoice",
            options = new[] { new { text = "Vor der Rezeptur", correct = true }, new { text = "Einmal am Tag", correct = false } },
        };

    /// <summary>A version that is ready to submit: its id, its course's, its chapter's and its question's.</summary>
    public sealed record WorkflowDraft(Guid Id, Guid CourseId, Guid Chapter, Guid Question);

    public sealed record WorkflowVersion(Guid Id, Guid CourseId, int Number, string Status, IReadOnlyList<WorkflowQuestion> Questions);

    public sealed record WorkflowQuestion(Guid Id, string Review);

    public sealed record WorkflowSummary(Guid Id, Guid CourseId, int Number, string Title, string Status);

    public sealed record WorkflowSummaryPage(IReadOnlyList<WorkflowSummary> Items, int TotalCount, int Page, int PageSize);
}
