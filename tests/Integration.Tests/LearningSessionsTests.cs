using System.Net;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.EditorialWorkflow;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningDatabase;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// Starting or resuming a learning session through the edge (story L1): a learner's first start creates the session
/// on the version Learning holds, every later one answers the same session until it is finished, and a session is
/// read only by its learner, who alone reports progress on it (story L2). Each test sends its own version's
/// announcement and waits until Learning handled it.
/// </summary>
public class LearningSessionsTests(AspireFixture aspire)
{
    private const int ConcurrentReports = 20;

    [Fact]
    public async Task Starting_a_session_answers_201_with_its_location_and_every_chapter_open()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = await PublishedToLearningAsync(cancellationToken);
        using var client = await aspire.CreateHostClientAsync("employee-a");

        using var started = await StartAsync(client, published.CourseVersionId, cancellationToken);

        started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));
        var session = await BodyOfAsync(started, cancellationToken);
        started.Headers.Location.ShouldNotBeNull().OriginalString.ShouldBe($"/learning/sessions/{session.Id}");
        session.CourseId.ShouldBe(published.CourseId);
        session.CourseVersionId.ShouldBe(published.CourseVersionId);
        session.Status.ShouldBe("InProgress");
        session.Chapters.ShouldBe(
            [.. published.Chapters
                .OrderBy(chapter => chapter.Position)
                .Select(chapter => new ChapterBody(chapter.ChapterId, chapter.Position, chapter.Kind, 0, false))]);

        using var read = await client.GetAsync(started.Headers.Location, cancellationToken);
        read.StatusCode.ShouldBe(HttpStatusCode.OK);
        ShouldDescribeTheSame(await BodyOfAsync(read, cancellationToken), session);
    }

    [Fact]
    public async Task Starting_twice_returns_the_same_session_with_200()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = await PublishedToLearningAsync(cancellationToken);
        using var client = await aspire.CreateHostClientAsync("employee-a");
        using var first = await StartAsync(client, published.CourseVersionId, cancellationToken);
        first.StatusCode.ShouldBe(HttpStatusCode.Created, await first.Content.ReadAsStringAsync(cancellationToken));

        using var again = await StartAsync(client, published.CourseVersionId, cancellationToken);

        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await BodyOfAsync(again, cancellationToken)).Id.ShouldBe((await BodyOfAsync(first, cancellationToken)).Id);
    }

    [Fact]
    public async Task Two_concurrent_starts_leave_one_session()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = await PublishedToLearningAsync(cancellationToken);
        using var client = await aspire.CreateHostClientAsync("employee-a");

        var responses = await Task.WhenAll(
            StartAsync(client, published.CourseVersionId, cancellationToken),
            StartAsync(client, published.CourseVersionId, cancellationToken));
        try
        {
            var created = responses.Where(response => response.StatusCode == HttpStatusCode.Created).ShouldHaveSingleItem();
            var createdId = (await BodyOfAsync(created, cancellationToken)).Id;
            var other = responses.Single(response => response != created);
            if (other.StatusCode == HttpStatusCode.OK)
            {
                (await BodyOfAsync(other, cancellationToken)).Id.ShouldBe(createdId);
            }
            else
            {
                other.StatusCode.ShouldBe(HttpStatusCode.Conflict);
                (await CodeOfAsync(other, cancellationToken)).ShouldBe("already-exists");
            }

            using var third = await StartAsync(client, published.CourseVersionId, cancellationToken);
            third.StatusCode.ShouldBe(HttpStatusCode.OK);
            (await BodyOfAsync(third, cancellationToken)).Id.ShouldBe(createdId);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        await using var connection = await OpenAsync(aspire, cancellationToken);
        await using var command = new Npgsql.NpgsqlCommand(
            "select count(*) from learning.mt_doc_learningsessionview where data->>'LearnerId' = @learner and data->>'CourseVersionId' = @version",
            connection);
        command.Parameters.AddWithValue("learner", SubjectOf(client).ToString());
        command.Parameters.AddWithValue("version", published.CourseVersionId.ToString());
        ((long)(await command.ExecuteScalarAsync(cancellationToken))!).ShouldBe(1);
    }

    [Fact]
    public async Task Another_person_starting_the_same_version_gets_a_session_of_their_own()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = await PublishedToLearningAsync(cancellationToken);
        using var employeeA = await aspire.CreateHostClientAsync("employee-a");
        using var employeeB = await aspire.CreateHostClientAsync("employee-b");

        using var startedByA = await StartAsync(employeeA, published.CourseVersionId, cancellationToken);
        using var startedByB = await StartAsync(employeeB, published.CourseVersionId, cancellationToken);

        startedByA.StatusCode.ShouldBe(HttpStatusCode.Created);
        startedByB.StatusCode.ShouldBe(HttpStatusCode.Created);
        (await BodyOfAsync(startedByB, cancellationToken)).Id.ShouldNotBe((await BodyOfAsync(startedByA, cancellationToken)).Id);
    }

    [Fact]
    public async Task Reading_another_persons_session_answers_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = await PublishedToLearningAsync(cancellationToken);
        using var employeeA = await aspire.CreateHostClientAsync("employee-a");
        using var employeeB = await aspire.CreateHostClientAsync("employee-b");
        using var started = await StartAsync(employeeA, published.CourseVersionId, cancellationToken);
        started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));

        using var read = await ReadSessionAsync(employeeB, (await BodyOfAsync(started, cancellationToken)).Id, cancellationToken);

        read.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(read, cancellationToken)).ShouldBe("learning.session-not-found");
    }

    [Fact]
    public async Task Starting_a_version_learning_does_not_hold_answers_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");

        using var unknown = await StartAsync(client, Guid.CreateVersion7(), cancellationToken);
        using var empty = await StartAsync(client, Guid.Empty, cancellationToken);

        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(unknown, cancellationToken)).ShouldBe("learning.course-version-not-found");
        empty.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(empty, cancellationToken)).ShouldBe("learning.course-version-not-found");
    }

    [Fact]
    public async Task Without_a_token_starting_and_reading_answer_401()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = aspire.CreateHostClient();

        using var started = await StartAsync(client, Guid.CreateVersion7(), cancellationToken);
        using var read = await ReadSessionAsync(client, Guid.CreateVersion7(), cancellationToken);

        started.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        read.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Without_a_token_reporting_answers_401()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = aspire.CreateHostClient();

        using var reported = await ReportProgressAsync(client, Guid.CreateVersion7(), Guid.CreateVersion7(), 50, cancellationToken);

        reported.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Reporting_progress_answers_200_with_the_chapter_at_its_new_percent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = await PublishedToLearningAsync(ShortVideoSeconds, cancellationToken);
        using var client = await aspire.CreateHostClientAsync("employee-a");
        var session = await StartedAsync(client, published, cancellationToken);
        var film = ChapterOf(published, "Video");
        await Task.Delay(ShortVideoWatched, cancellationToken);

        using var reported = await ReportProgressAsync(client, session.Id, film, 60, cancellationToken);

        reported.StatusCode.ShouldBe(HttpStatusCode.OK, await reported.Content.ReadAsStringAsync(cancellationToken));
        var body = await BodyOfAsync(reported, cancellationToken);
        body.Id.ShouldBe(session.Id);
        body.Status.ShouldBe("InProgress");
        var chapter = body.Chapters.Single(chapter => chapter.ChapterId == film);
        chapter.ProgressPercent.ShouldBe(60);
        chapter.Completed.ShouldBeFalse();
        body.Chapters.Where(other => other.ChapterId != film).ShouldAllBe(other => other.ProgressPercent == 0 && !other.Completed);

        using var read = await ReadSessionAsync(client, session.Id, cancellationToken);
        ShouldDescribeTheSame(await BodyOfAsync(read, cancellationToken), body);
    }

    [Fact]
    public async Task An_older_or_repeated_report_answers_200_and_stores_no_event()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = await PublishedToLearningAsync(ShortVideoSeconds, cancellationToken);
        using var client = await aspire.CreateHostClientAsync("employee-a");
        var session = await StartedAsync(client, published, cancellationToken);
        var film = ChapterOf(published, "Video");
        await Task.Delay(ShortVideoWatched, cancellationToken);
        using (var first = await ReportProgressAsync(client, session.Id, film, 60, cancellationToken))
        {
            first.StatusCode.ShouldBe(HttpStatusCode.OK, await first.Content.ReadAsStringAsync(cancellationToken));
        }

        var stored = await EventsInAsync(session.Id, cancellationToken);

        using var older = await ReportProgressAsync(client, session.Id, film, 40, cancellationToken);
        using var repeated = await ReportProgressAsync(client, session.Id, film, 60, cancellationToken);

        older.StatusCode.ShouldBe(HttpStatusCode.OK, await older.Content.ReadAsStringAsync(cancellationToken));
        repeated.StatusCode.ShouldBe(HttpStatusCode.OK, await repeated.Content.ReadAsStringAsync(cancellationToken));
        (await BodyOfAsync(older, cancellationToken)).Chapters.Single(chapter => chapter.ChapterId == film).ProgressPercent.ShouldBe(60);
        (await BodyOfAsync(repeated, cancellationToken)).Chapters.Single(chapter => chapter.ChapterId == film).ProgressPercent.ShouldBe(60);
        (await EventsInAsync(session.Id, cancellationToken)).ShouldBe(stored);
    }

    [Fact]
    public async Task Text_and_pdf_complete_at_100_and_video_at_95()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = await PublishedToLearningAsync(ShortVideoSeconds, cancellationToken);
        using var client = await aspire.CreateHostClientAsync("employee-a");
        var session = await StartedAsync(client, published, cancellationToken);

        foreach (var (kind, below, threshold) in new[] { ("Text", 99, 100), ("Pdf", 99, 100), ("Video", 94, 95) })
        {
            var chapter = ChapterOf(published, kind);
            await WatchedIfVideoAsync(kind, cancellationToken);

            using var belowThreshold = await ReportProgressAsync(client, session.Id, chapter, below, cancellationToken);
            belowThreshold.StatusCode.ShouldBe(HttpStatusCode.OK, await belowThreshold.Content.ReadAsStringAsync(cancellationToken));
            (await BodyOfAsync(belowThreshold, cancellationToken)).Chapters.Single(answered => answered.ChapterId == chapter).Completed.ShouldBeFalse($"{kind} at {below} %");

            await WatchedIfVideoAsync(kind, cancellationToken);
            using var reached = await ReportProgressAsync(client, session.Id, chapter, threshold, cancellationToken);
            reached.StatusCode.ShouldBe(HttpStatusCode.OK, await reached.Content.ReadAsStringAsync(cancellationToken));
            (await BodyOfAsync(reached, cancellationToken)).Chapters.Single(answered => answered.ChapterId == chapter).Completed.ShouldBeTrue($"{kind} at {threshold} %");
        }

        using var read = await ReadSessionAsync(client, session.Id, cancellationToken);
        (await BodyOfAsync(read, cancellationToken)).Status.ShouldBe("ContentCompleted");
    }

    [Fact]
    public async Task Reporting_on_another_persons_session_answers_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = await PublishedToLearningAsync(cancellationToken);
        using var employeeA = await aspire.CreateHostClientAsync("employee-a");
        using var employeeB = await aspire.CreateHostClientAsync("employee-b");
        var session = await StartedAsync(employeeA, published, cancellationToken);
        var stored = await EventsInAsync(session.Id, cancellationToken);

        using var reported = await ReportProgressAsync(employeeB, session.Id, ChapterOf(published, "Text"), 100, cancellationToken);

        reported.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(reported, cancellationToken)).ShouldBe("learning.session-not-found");
        (await EventsInAsync(session.Id, cancellationToken)).ShouldBe(stored);
    }

    [Fact]
    public async Task Reporting_on_an_unknown_or_empty_session_answers_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");

        using var unknown = await ReportProgressAsync(client, Guid.CreateVersion7(), Guid.CreateVersion7(), 50, cancellationToken);
        using var empty = await ReportProgressAsync(client, Guid.Empty, Guid.CreateVersion7(), 50, cancellationToken);

        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(unknown, cancellationToken)).ShouldBe("learning.session-not-found");
        empty.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await CodeOfAsync(empty, cancellationToken)).ShouldBe("learning.session-not-found");
    }

    [Fact]
    public async Task A_chapter_of_another_version_answers_422()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = await PublishedToLearningAsync(cancellationToken);
        var other = await PublishedToLearningAsync(cancellationToken);
        using var client = await aspire.CreateHostClientAsync("employee-a");
        var session = await StartedAsync(client, published, cancellationToken);
        var stored = await EventsInAsync(session.Id, cancellationToken);

        using var reported = await ReportProgressAsync(client, session.Id, ChapterOf(other, "Text"), 100, cancellationToken);

        reported.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await reported.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(reported, cancellationToken)).ShouldBe("learning.chapter-not-in-course");
        (await EventsInAsync(session.Id, cancellationToken)).ShouldBe(stored);
    }

    [Fact]
    public async Task A_percent_outside_0_to_100_or_missing_answers_400_invalid_argument()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = await PublishedToLearningAsync(cancellationToken);
        using var client = await aspire.CreateHostClientAsync("employee-a");
        var session = await StartedAsync(client, published, cancellationToken);
        var text = ChapterOf(published, "Text");

        using var above = await ReportProgressAsync(client, session.Id, text, 101, cancellationToken);
        using var below = await ReportProgressAsync(client, session.Id, text, -1, cancellationToken);
        using var noChapter = await ReportProgressAsync(client, session.Id, Guid.Empty, 50, cancellationToken);
        using var noPercent = await client.PostAsJsonAsync(new Uri($"/learning/sessions/{session.Id}/progress", UriKind.Relative), new { chapterId = text }, cancellationToken);

        above.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await above.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(above, cancellationToken)).ShouldBe("invalid-argument");
        below.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await below.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(below, cancellationToken)).ShouldBe("invalid-argument");
        noChapter.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await noChapter.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(noChapter, cancellationToken)).ShouldBe("invalid-argument");
        noPercent.StatusCode.ShouldBe(HttpStatusCode.BadRequest, await noPercent.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(noPercent, cancellationToken)).ShouldBe("invalid-argument");
    }

    /// <summary>
    /// Reports racing on one session load the same version of its stream; only one append lands, and every other one is
    /// told the session changed. A report on the video that loads after another report landed measures its pace from
    /// that report (ADR 0021) and may be refused as too fast. Each chapter completes, and the content, at most once
    /// whatever the interleaving, and nothing answers 500.
    /// </summary>
    [Fact]
    public async Task Concurrent_reports_on_one_session_answer_200_or_409_and_never_500()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = await PublishedToLearningAsync(ShortVideoSeconds, cancellationToken);
        using var client = await aspire.CreateHostClientAsync("employee-a");
        var session = await StartedAsync(client, published, cancellationToken);
        var chapters = published.Chapters.OrderBy(chapter => chapter.Position).Select(chapter => chapter.ChapterId).ToList();
        var film = ChapterOf(published, "Video");
        await Task.Delay(ShortVideoWatched, cancellationToken);

        var reports = Enumerable.Range(0, ConcurrentReports).Select(report => chapters[report % chapters.Count]).ToList();
        var responses = await Task.WhenAll(reports
            .Select((chapter, report) => ReportProgressAsync(client, session.Id, chapter, Math.Min(100, 10 + (5 * report)), cancellationToken)));
        try
        {
            foreach (var (response, chapter) in responses.Zip(reports))
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (chapter == film)
                {
                    response.StatusCode.ShouldBeOneOf([HttpStatusCode.OK, HttpStatusCode.Conflict, HttpStatusCode.UnprocessableEntity], body);
                }
                else
                {
                    response.StatusCode.ShouldBeOneOf([HttpStatusCode.OK, HttpStatusCode.Conflict], body);
                }

                if (response.StatusCode == HttpStatusCode.Conflict)
                {
                    (await CodeOfAsync(response, cancellationToken)).ShouldBe("learning.session-changed");
                }

                if (response.StatusCode == HttpStatusCode.UnprocessableEntity)
                {
                    (await CodeOfAsync(response, cancellationToken)).ShouldBe("learning.progress-too-fast");
                }
            }
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }

        (await CountAsync(aspire, "select count(*) from learning.mt_events where stream_id = @id and type = 'content_completed'", session.Id, cancellationToken))
            .ShouldBeLessThanOrEqualTo(1);
        var completedChapters = await CountAsync(aspire, "select count(*) from learning.mt_events where stream_id = @id and type = 'chapter_completed'", session.Id, cancellationToken);
        completedChapters.ShouldBeLessThanOrEqualTo(chapters.Count);
        foreach (var chapter in chapters)
        {
            (await ChapterCompletionsAsync(session.Id, chapter, cancellationToken)).ShouldBeLessThanOrEqualTo(1, $"chapter {chapter}");
        }

        using var read = await ReadSessionAsync(client, session.Id, cancellationToken);
        (await BodyOfAsync(read, cancellationToken)).Chapters.Count(chapter => chapter.Completed).ShouldBe((int)completedChapters);
    }

    /// <summary>The issue's done-when (#43): a jump from 0 to 95 % of a 420 s video at once is refused and stores nothing.</summary>
    [Fact]
    public async Task A_video_reported_at_95_percent_seconds_after_the_start_answers_422_and_stores_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = await PublishedToLearningAsync(cancellationToken);
        using var client = await aspire.CreateHostClientAsync("employee-a");
        var session = await StartedAsync(client, published, cancellationToken);
        var film = ChapterOf(published, "Video");
        var stored = await EventsInAsync(session.Id, cancellationToken);

        using var reported = await ReportProgressAsync(client, session.Id, film, 95, cancellationToken);

        reported.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await reported.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(reported, cancellationToken)).ShouldBe("learning.progress-too-fast");
        (await EventsInAsync(session.Id, cancellationToken)).ShouldBe(stored);
        using var read = await ReadSessionAsync(client, session.Id, cancellationToken);
        var chapter = (await BodyOfAsync(read, cancellationToken)).Chapters.Single(answered => answered.ChapterId == film);
        chapter.ProgressPercent.ShouldBe(0);
        chapter.Completed.ShouldBeFalse();
    }

    /// <summary>A refusal heals itself (ADR 0021): it stores nothing, so the next report measures from the same instant and passes once the video could have been watched.</summary>
    [Fact]
    public async Task A_refused_video_report_is_recorded_once_its_playback_time_has_passed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var published = await PublishedToLearningAsync(ShortVideoSeconds, cancellationToken);
        using var client = await aspire.CreateHostClientAsync("employee-a");
        var session = await StartedAsync(client, published, cancellationToken);
        var film = ChapterOf(published, "Video");

        using var atOnce = await ReportProgressAsync(client, session.Id, film, 95, cancellationToken);
        atOnce.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity, await atOnce.Content.ReadAsStringAsync(cancellationToken));
        (await CodeOfAsync(atOnce, cancellationToken)).ShouldBe("learning.progress-too-fast");

        await Task.Delay(ShortVideoWatched, cancellationToken);
        using var watched = await ReportProgressAsync(client, session.Id, film, 95, cancellationToken);

        watched.StatusCode.ShouldBe(HttpStatusCode.OK, await watched.Content.ReadAsStringAsync(cancellationToken));
        var chapter = (await BodyOfAsync(watched, cancellationToken)).Chapters.Single(answered => answered.ChapterId == film);
        chapter.ProgressPercent.ShouldBe(95);
        chapter.Completed.ShouldBeTrue();
    }

    /// <summary>Lets a <see cref="ShortVideoSeconds"/> video's playback time pass before a report on it; text and PDF are not paced.</summary>
    private static Task WatchedIfVideoAsync(string kind, CancellationToken cancellationToken) =>
        kind == "Video" ? Task.Delay(ShortVideoWatched, cancellationToken) : Task.CompletedTask;

    private static Guid ChapterOf(PublishedEvent published, string kind) => published.Chapters.Single(chapter => chapter.Kind == kind).ChapterId;

    private static async Task<SessionBody> StartedAsync(HttpClient client, PublishedEvent published, CancellationToken cancellationToken)
    {
        using var started = await StartAsync(client, published.CourseVersionId, cancellationToken);
        started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));
        return await BodyOfAsync(started, cancellationToken);
    }

    private Task<long> EventsInAsync(Guid session, CancellationToken cancellationToken) =>
        CountAsync(aspire, "select count(*) from learning.mt_events where stream_id = @id", session, cancellationToken);

    private async Task<long> ChapterCompletionsAsync(Guid session, Guid chapter, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(aspire, cancellationToken);
        await using var command = new Npgsql.NpgsqlCommand(
            "select count(*) from learning.mt_events where stream_id = @id and type = 'chapter_completed' and data->>'Chapter' = @chapter",
            connection);
        command.Parameters.AddWithValue("id", session);
        command.Parameters.AddWithValue("chapter", chapter.ToString());
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private Task<PublishedEvent> PublishedToLearningAsync(CancellationToken cancellationToken) =>
        PublishedToLearningAsync(420, cancellationToken);

    private async Task<PublishedEvent> PublishedToLearningAsync(int videoSeconds, CancellationToken cancellationToken)
    {
        var published = Published(Guid.CreateVersion7(), videoSeconds);
        await DeliverAsync(aspire, published, cancellationToken);
        return published;
    }
}
