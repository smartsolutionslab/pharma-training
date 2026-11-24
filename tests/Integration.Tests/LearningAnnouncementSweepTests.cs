using System.Net;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningDatabase;
using static SmartSolutionsLab.PharmaTraining.Integration.Tests.LearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// A content completion Learning stored but never announced is announced by its sweep (#277). The crash between the
/// report's append and its outbox commit is simulated by its outcome: a <c>ContentCompleted</c> in the session's stream,
/// with no marker and no envelope. A report that announces its completion records the marker in the same commit, so the
/// sweep never announces it again.
/// </summary>
public class LearningAnnouncementSweepTests(AspireFixture aspire)
{
    [Fact]
    public async Task A_stored_content_completion_that_was_never_announced_opens_the_examination()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");
        var learner = SubjectOf(client);
        await MemberAsync(aspire, learner, cancellationToken);
        var published = Published(Guid.CreateVersion7(), ShortVideoSeconds);
        await DeliverAsync(aspire, published, cancellationToken);
        var session = await StartedAsync(client, published, cancellationToken);

        var version = await PlantCompletionAsync(
            aspire,
            new PlantedContentCompleted(session.Id, learner, published.CourseId, published.CourseVersionId, TimeProvider.System.GetUtcNow()),
            cancellationToken);

        await AnnouncementSweeps.WaitUntilAsync(
            async () =>
            {
                using var read = await client.GetAsync(AssessmentAttempts.BySession(session.Id), cancellationToken);
                return read.StatusCode == HttpStatusCode.OK;
            },
            $"Assessment opened no examination for session {session.Id}, whose stored completion was never announced,",
            cancellationToken);
        (await CountAnnouncedAsync(aspire, session.Id, version, cancellationToken)).ShouldBe(1);
        (await AssessmentDatabase.CountAsync(aspire, "select count(*) from assessment.handled_content_completions where session_id = @id", session.Id, cancellationToken))
            .ShouldBe(1);
    }

    [Fact]
    public async Task A_report_that_completes_the_content_records_its_announcement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = await aspire.CreateHostClientAsync("employee-a");
        await MemberAsync(aspire, SubjectOf(client), cancellationToken);
        var published = Published(Guid.CreateVersion7(), ShortVideoSeconds);
        await DeliverAsync(aspire, published, cancellationToken);
        var session = await StartedAsync(client, published, cancellationToken);
        await ReportOkAsync(client, session.Id, ChapterOf(published, "Text"), 100, cancellationToken);
        await ReportOkAsync(client, session.Id, ChapterOf(published, "Pdf"), 100, cancellationToken);
        await Task.Delay(ShortVideoWatched, cancellationToken);

        await ReportOkAsync(client, session.Id, ChapterOf(published, "Video"), 95, cancellationToken);

        var version = await StoredVersionAsync(aspire, session.Id, "content_completed", cancellationToken);
        (await CountAnnouncedAsync(aspire, session.Id, version, cancellationToken)).ShouldBe(1, "the report's commit did not record the announcement it carried");
    }

    private static async Task<SessionBody> StartedAsync(HttpClient client, PublishedEvent published, CancellationToken cancellationToken)
    {
        using var started = await StartAsync(client, published.CourseVersionId, cancellationToken);
        started.StatusCode.ShouldBe(HttpStatusCode.Created, await started.Content.ReadAsStringAsync(cancellationToken));
        return await BodyOfAsync(started, cancellationToken);
    }

    private static async Task ReportOkAsync(HttpClient client, Guid session, Guid chapter, int percent, CancellationToken cancellationToken)
    {
        using var reported = await ReportProgressAsync(client, session, chapter, percent, cancellationToken);
        reported.StatusCode.ShouldBe(HttpStatusCode.OK, await reported.Content.ReadAsStringAsync(cancellationToken));
    }

    private static Guid ChapterOf(PublishedEvent published, string kind) => published.Chapters.Single(chapter => chapter.Kind == kind).ChapterId;
}
