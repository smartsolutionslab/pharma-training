using System.Text.Json.Nodes;

namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>
/// Brings a course version to Published through Editorial, as three people so four eyes holds whether it is on or off: the
/// author drafts, adds the chapters, sets the blueprint before any approval (docs/adr/0007), adds the questions and
/// submits; the approver approves every question; the publisher publishes. A version found in the list is continued from
/// its status: a draft gets only its missing chapters and questions (by title and text), a published one nothing.
/// </summary>
internal sealed class CourseSeeding(DemoEditors editors, EdgeClient edge, SeedCount count, TextWriter output)
{
    private readonly Dictionary<(string Title, int Number), SeededVersion> versions = [];

    public SeededVersion Version(string title, int number) =>
        versions.TryGetValue((title, number), out var version) ? version : throw new SeedingException($"{title} has no version {number} in the seed.");

    public async Task EnsureAsync(DemoCourse course, int number, CancellationToken cancellationToken)
    {
        var listed = await edge.AllAsync(editors.Author, "/editorial/course-versions", cancellationToken);
        var summary = listed.FirstOrDefault(version => Json.Text(version, "title") == course.Title && Json.Int(version, "number") == number);
        var status = Json.Text(summary, "status");
        if (summary is not null && status is "Published" or "Superseded" or "Retired")
        {
            versions[(course.Title, number)] = new SeededVersion(Json.Required(summary, "id", "The course versions"), Json.Required(summary, "courseId", "The course versions"));
            return;
        }

        string id;
        if (summary is null)
        {
            id = number == 1
                ? await edge.CreateAsync(editors.Author, "/editorial/courses", new JsonObject { ["title"] = course.Title }, cancellationToken)
                : await edge.CreateAsync(editors.Author, $"/editorial/course-versions/{Version(course.Title, number - 1).Id}/next", null, cancellationToken);
            count.Add();
            status = "Draft";
            output.WriteLine($"Demo data: drafted {course.Title}, version {number}.");
        }
        else
        {
            id = Json.Required(summary, "id", "The course versions");
        }

        var detail = await edge.GetAsync(editors.Author, $"/editorial/course-versions/{id}", cancellationToken);
        if (status == "Draft" && await CompleteDraftAsync(course, number, id, detail, cancellationToken))
        {
            // What the draft holds now: a new blueprint sends the questions a next version copied approved back to review.
            detail = await edge.GetAsync(editors.Author, $"/editorial/course-versions/{id}", cancellationToken);
        }

        var unapproved = Json.Items(detail, "questions").Where(question => Json.Text(question, "review") != "Approved").Select(question => Json.Required(question, "id", "The course version"));
        foreach (var question in unapproved)
        {
            await edge.CallAsync(editors.Approver, HttpMethod.Post, $"/editorial/course-versions/{id}/questions/{question}/approve", null, cancellationToken);
        }

        if (status == "Draft")
        {
            await edge.CallAsync(editors.Author, HttpMethod.Post, $"/editorial/course-versions/{id}/submit", null, cancellationToken);
        }

        await edge.CallAsync(editors.Publisher, HttpMethod.Post, $"/editorial/course-versions/{id}/publish", null, cancellationToken);
        output.WriteLine($"Demo data: published {course.Title}, version {number}.");
        versions[(course.Title, number)] = new SeededVersion(id, Json.Required(detail, "courseId", "The course version"));
    }

    /// <summary>
    /// Adds the draft's missing chapters, its blueprint if it differs, then its missing questions; answers whether it changed
    /// anything. The blueprint asks every question the version holds, as Editorial requires of a version it lets submit
    /// and publish; its passing score and validity are the plan's.
    /// </summary>
    private async Task<bool> CompleteDraftAsync(DemoCourse course, int number, string id, JsonNode detail, CancellationToken cancellationToken)
    {
        var chapters = number == 1 ? course.Chapters : [.. course.Chapters, .. course.SecondVersionChapters];
        var changed = false;
        var chapterIds = Json.Items(detail, "chapters").ToDictionary(chapter => Json.Required(chapter, "title", "The course version"), chapter => Json.Required(chapter, "id", "The course version"), StringComparer.Ordinal);
        foreach (var chapter in chapters.Where(chapter => !chapterIds.ContainsKey(chapter.Title)))
        {
            chapterIds[chapter.Title] = await edge.CreateAsync(
                editors.Author,
                $"/editorial/course-versions/{id}/chapters",
                new JsonObject { ["title"] = chapter.Title, ["kind"] = "Text", ["text"] = chapter.Text },
                cancellationToken);
            count.Add();
            changed = true;
        }

        var questionCount = chapters.Sum(chapter => chapter.Questions.Count);
        var blueprint = detail["blueprint"];
        if (Json.Int(blueprint, "questions") != questionCount
            || Json.Int(blueprint, "passingScorePercent") != course.Blueprint.PassingScorePercent
            || Json.Int(blueprint, "validityMonths") != course.Blueprint.ValidityMonths)
        {
            var planned = new JsonObject
            {
                ["questions"] = questionCount,
                ["passingScorePercent"] = course.Blueprint.PassingScorePercent,
                ["validityMonths"] = course.Blueprint.ValidityMonths,
            };
            await edge.CallAsync(editors.Author, HttpMethod.Put, $"/editorial/course-versions/{id}/blueprint", planned, cancellationToken);
            changed = true;
        }

        var asked = Json.Items(detail, "questions").Select(question => Json.Text(question, "text")).ToHashSet(StringComparer.Ordinal);
        foreach (var chapter in chapters)
        {
            foreach (var question in chapter.Questions.Where(question => !asked.Contains(question.Text)))
            {
                var body = new JsonObject
                {
                    ["chapterId"] = chapterIds[chapter.Title],
                    ["text"] = question.Text,
                    ["type"] = "SingleChoice",
                    ["options"] = new JsonArray([.. question.Options.Select(option => (JsonNode)new JsonObject { ["text"] = option.Text, ["correct"] = option.Correct })]),
                };
                await edge.CreateAsync(editors.Author, $"/editorial/course-versions/{id}/questions", body, cancellationToken);
                count.Add();
                changed = true;
            }
        }

        return changed;
    }
}
