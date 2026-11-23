using System.Net;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Contracts.Learning;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Commands.ReportProgress;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Consumers;
using static SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Sessions.InMemoryLearningSessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Sessions;

/// <summary>
/// A learner reports how far they got in one chapter of their own session (story L2): only real progress is stored,
/// a chapter counts once its kind's threshold is reached, and nothing is told about other people's sessions. Video
/// progress may not outpace playback (#43, ADR 0024): the film lasts 420 s in Learning's copy, so a test that reports
/// a gain on it lets the film's length pass first.
/// </summary>
public class ReportProgressTests
{
    private static readonly ChapterId Intro = ChapterId.From(Guid.CreateVersion7());
    private static readonly ChapterId Handbook = ChapterId.From(Guid.CreateVersion7());
    private static readonly ChapterId Film = ChapterId.From(Guid.CreateVersion7());
    private static readonly TimeSpan FilmDuration = TimeSpan.FromSeconds(CourseVersionCopies.VideoSeconds);

    private readonly InMemoryLearningSessions sessions = new();
    private readonly InMemoryCourseVersionCopies copies = new();
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly PersonId learner = PersonId.From(Guid.CreateVersion7());
    private readonly CourseId course = CourseId.From(Guid.CreateVersion7());
    private readonly CourseVersionId version = CourseVersionId.From(Guid.CreateVersion7());

    public ReportProgressTests() => copies.Hold(CopyOf(version, CourseVersionCopies.VideoSeconds));

    [Fact]
    public async Task Progress_on_ones_own_session_is_stored()
    {
        var session = await StartedAsync();
        var saves = sessions.Saves;
        time.Advance(TimeSpan.FromMinutes(5));

        var reported = await ReportAsync(session, Film, 60);

        reported.IsSuccess.ShouldBeTrue();
        sessions.Saves.ShouldBe(saves + 1);
        var recorded = sessions.Streams[session.Value].OfType<ChapterProgressRecorded>().ShouldHaveSingleItem();
        recorded.Chapter.ShouldBe(Film);
        recorded.Progress.ShouldBe(ProgressPercent.From(60));
        recorded.RecordedAt.ShouldBe(Timestamp.From(time.GetUtcNow()));
    }

    [Fact]
    public async Task An_older_or_repeated_report_stores_nothing()
    {
        var session = await StartedAsync();
        time.Advance(FilmDuration);
        (await ReportAsync(session, Film, 60)).IsSuccess.ShouldBeTrue();
        var stored = sessions.Streams[session.Value].Count;
        var saves = sessions.Saves;

        var older = await ReportAsync(session, Film, 40);
        var repeated = await ReportAsync(session, Film, 60);

        older.IsSuccess.ShouldBeTrue();
        repeated.IsSuccess.ShouldBeTrue();
        sessions.Streams[session.Value].Count.ShouldBe(stored);
        sessions.Saves.ShouldBe(saves);
    }

    [Fact]
    public async Task A_video_chapter_completes_at_95_percent_and_a_text_chapter_at_100()
    {
        var session = await StartedAsync();

        time.Advance(FilmDuration);
        await ReportAsync(session, Film, 94);
        CompletedChapters(session).ShouldBeEmpty();
        time.Advance(FilmDuration);
        await ReportAsync(session, Film, 95);
        CompletedChapters(session).ShouldBe([Film]);

        await ReportAsync(session, Intro, 99);
        CompletedChapters(session).ShouldBe([Film]);
        await ReportAsync(session, Intro, 100);
        CompletedChapters(session).ShouldBe([Film, Intro]);

        await ReportAsync(session, Handbook, 99);
        CompletedChapters(session).ShouldBe([Film, Intro]);
        await ReportAsync(session, Handbook, 100);
        CompletedChapters(session).ShouldBe([Film, Intro, Handbook]);
    }

    [Fact]
    public async Task Another_persons_session_is_not_found()
    {
        var session = await StartedAsync();
        var stored = sessions.Streams[session.Value].Count;
        var saves = sessions.Saves;

        var reported = await ReportAsync(session, PersonId.From(Guid.CreateVersion7()), Film, 60);

        reported.IsFailure.ShouldBeTrue();
        reported.Error.Code.ShouldBe("learning.session-not-found");
        reported.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        sessions.Streams[session.Value].Count.ShouldBe(stored);
        sessions.Saves.ShouldBe(saves);
    }

    [Fact]
    public async Task An_unknown_session_is_not_found()
    {
        var unknown = LearningSessionId.New();

        var reported = await ReportAsync(unknown, Film, 60);

        reported.IsFailure.ShouldBeTrue();
        reported.Error.Code.ShouldBe("learning.session-not-found");
        reported.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        sessions.Streams.ShouldBeEmpty();
        sessions.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task A_chapter_outside_the_session_is_refused_with_422()
    {
        var session = await StartedAsync();
        var saves = sessions.Saves;

        var reported = await ReportAsync(session, ChapterId.From(Guid.CreateVersion7()), 100);

        reported.IsFailure.ShouldBeTrue();
        reported.Error.Code.ShouldBe("learning.chapter-not-in-course");
        reported.Error.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        sessions.Saves.ShouldBe(saves);
    }

    [Fact]
    public async Task A_finished_session_refuses_progress_with_409()
    {
        var session = await StartedAsync();
        await ChangeAsync(session, started => started.Finish(Now()));
        var stored = sessions.Streams[session.Value].Count;
        var saves = sessions.Saves;

        var reported = await ReportAsync(session, Film, 60);

        reported.IsFailure.ShouldBeTrue();
        reported.Error.Code.ShouldBe("learning.session-finished");
        reported.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        sessions.Streams[session.Value].Count.ShouldBe(stored);
        sessions.Saves.ShouldBe(saves);
    }

    [Fact]
    public async Task Completing_the_last_chapter_publishes_ContentCompletedV1_once_with_its_ids()
    {
        var session = await StartedAsync();
        await ReportAsync(session, Intro, 100);
        await ReportAsync(session, Handbook, 100);
        sessions.Published.ShouldBeEmpty();
        time.Advance(FilmDuration);

        var reported = await ReportAsync(session, Film, 95);

        reported.IsSuccess.ShouldBeTrue();
        var completed = sessions.Published.ShouldHaveSingleItem().ShouldBeOfType<ContentCompletedV1>();
        completed.SessionId.ShouldBe(session.Value);
        completed.PersonId.ShouldBe(learner.Value);
        completed.CourseId.ShouldBe(course.Value);
        completed.CourseVersionId.ShouldBe(version.Value);
        sessions.Commits.ShouldBe(1);
        sessions.Streams[session.Value].OfType<ContentCompleted>().ShouldHaveSingleItem();

        // The session is stored before its announcement is published and committed.
        sessions.Steps.ShouldBe([Saved, Saved, Saved, Queued, Committed]);
    }

    [Fact]
    public async Task A_report_after_content_completed_publishes_nothing()
    {
        var session = await CompletedAsync();
        var saves = sessions.Saves;
        time.Advance(FilmDuration);

        var reported = await ReportAsync(session, Film, 100);

        reported.IsSuccess.ShouldBeTrue();
        sessions.Saves.ShouldBe(saves + 1);
        sessions.Published.Count.ShouldBe(1);
        sessions.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_report_that_completes_nothing_publishes_nothing_and_commits_nothing()
    {
        var session = await StartedAsync();
        time.Advance(FilmDuration);

        var reported = await ReportAsync(session, Film, 60);

        reported.IsSuccess.ShouldBeTrue();
        sessions.Published.ShouldBeEmpty();
        sessions.Commits.ShouldBe(0);
        sessions.Steps.ShouldBe([Saved]);
    }

    [Fact]
    public async Task After_chapters_reopen_completing_them_publishes_ContentCompletedV1_again()
    {
        var session = await CompletedAsync();
        await ChangeAsync(session, completed => completed.ReopenChapters([Intro], Now()));

        await ReportAsync(session, Intro, 99);
        sessions.Published.Count.ShouldBe(1);
        var reported = await ReportAsync(session, Intro, 100);

        reported.IsSuccess.ShouldBeTrue();
        sessions.Published.Count.ShouldBe(2);
        sessions.Published.OfType<ContentCompletedV1>().Select(completed => completed.SessionId).ShouldBe([session.Value, session.Value]);
        sessions.Commits.ShouldBe(2);
        sessions.Streams[session.Value].OfType<ContentCompleted>().Count().ShouldBe(2);
    }

    [Fact]
    public async Task A_refused_report_publishes_nothing()
    {
        var session = await StartedAsync();
        await ReportAsync(session, Intro, 100);
        await ReportAsync(session, Handbook, 100);
        sessions.Steps.Clear();

        var outside = await ReportAsync(session, ChapterId.From(Guid.CreateVersion7()), 100);
        var someoneElse = await ReportAsync(session, PersonId.From(Guid.CreateVersion7()), Film, 100);
        var unknown = await ReportAsync(LearningSessionId.New(), Film, 100);

        outside.IsFailure.ShouldBeTrue();
        someoneElse.IsFailure.ShouldBeTrue();
        unknown.IsFailure.ShouldBeTrue();
        sessions.Published.ShouldBeEmpty();
        sessions.Commits.ShouldBe(0);
        sessions.Steps.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_report_that_loses_the_race_answers_409_session_changed()
    {
        var session = await StartedAsync();
        var stored = sessions.Streams[session.Value].Count;
        time.Advance(FilmDuration);
        sessions.LoseNextSave();

        var reported = await ReportAsync(session, Film, 60);

        reported.IsFailure.ShouldBeTrue("the lost append was answered as a success");
        reported.Error.ShouldBe(LearningSessionFailures.Changed(session.Value));
        reported.Error.Code.ShouldBe("learning.session-changed");
        reported.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        sessions.Streams[session.Value].Count.ShouldBe(stored);
    }

    [Fact]
    public async Task A_report_that_loses_the_race_publishes_nothing()
    {
        var session = await StartedAsync();
        await ReportAsync(session, Intro, 100);
        await ReportAsync(session, Handbook, 100);
        sessions.Steps.Clear();
        time.Advance(FilmDuration);
        sessions.LoseNextSave();

        var reported = await ReportAsync(session, Film, 95);

        reported.IsFailure.ShouldBeTrue("the lost append was answered as a success");
        reported.Error.Code.ShouldBe("learning.session-changed");
        sessions.Published.ShouldBeEmpty();
        sessions.Commits.ShouldBe(0);
        sessions.Steps.ShouldBeEmpty();
        sessions.Streams[session.Value].OfType<ContentCompleted>().ShouldBeEmpty();
    }

    /// <summary>
    /// The player reports the end of the last chapter and the tab closes. Once the session is stored, its completion is
    /// in the stream and no later report stores anything, so the announcement must be committed even though the caller
    /// has gone, or it is never published.
    /// </summary>
    [Fact]
    public async Task Completing_the_content_is_announced_even_when_the_caller_goes_away_after_the_save()
    {
        var session = await StartedAsync();
        await ReportAsync(session, Intro, 100);
        await ReportAsync(session, Handbook, 100);
        time.Advance(FilmDuration);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        sessions.CancelAfterNextSave(caller);

        await Handler().HandleAsync(new ReportProgress(session, learner, Film, ProgressPercent.From(95)), caller.Token);

        sessions.Published.ShouldHaveSingleItem().ShouldBeOfType<ContentCompletedV1>().SessionId.ShouldBe(session.Value);
        sessions.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_video_jump_to_95_percent_seconds_after_the_start_is_refused_with_422_and_stores_nothing()
    {
        var session = await StartedAsync();
        var stored = sessions.Streams[session.Value].Count;
        var saves = sessions.Saves;
        time.Advance(TimeSpan.FromSeconds(5));

        var reported = await ReportAsync(session, Film, 95);

        reported.IsFailure.ShouldBeTrue("a jump to 95 % five seconds after the start was accepted");
        reported.Error.Code.ShouldBe("learning.progress-too-fast");
        reported.Error.Status.ShouldBe(HttpStatusCode.UnprocessableEntity);
        sessions.Streams[session.Value].Count.ShouldBe(stored);
        sessions.Saves.ShouldBe(saves);
        sessions.Published.ShouldBeEmpty();
        sessions.Commits.ShouldBe(0);
        sessions.Steps.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_video_reported_after_its_playback_time_is_recorded()
    {
        var session = await StartedAsync();
        time.Advance(TimeSpan.FromSeconds(320));

        var reported = await ReportAsync(session, Film, 95);

        reported.IsSuccess.ShouldBeTrue();
        CompletedChapters(session).ShouldBe([Film]);
    }

    [Fact]
    public async Task The_duration_comes_from_the_copy_of_the_sessions_course_version()
    {
        // The other version, held from the constructor, has the same film at 420 s; the session's lasts 60 s.
        var shortVersion = CourseVersionId.From(Guid.CreateVersion7());
        copies.Hold(CopyOf(shortVersion, 60));
        var session = await StartedAsync(shortVersion);

        // 95 points of 60 s need 45.6 s.
        time.Advance(TimeSpan.FromSeconds(45));
        var early = await ReportAsync(session, Film, 95);
        time.Advance(TimeSpan.FromSeconds(1));
        var reported = await ReportAsync(session, Film, 95);

        early.IsFailure.ShouldBeTrue("95 % of a 60 s film after 45 s was accepted");
        early.Error.Code.ShouldBe("learning.progress-too-fast");
        reported.IsSuccess.ShouldBeTrue("95 % of a 60 s film after 46 s was refused");
        CompletedChapters(session).ShouldBe([Film]);
    }

    [Fact]
    public async Task A_refused_report_is_accepted_once_enough_time_has_passed()
    {
        var session = await StartedAsync();
        time.Advance(TimeSpan.FromSeconds(5));
        var refused = await ReportAsync(session, Film, 95);
        time.Advance(TimeSpan.FromSeconds(315));

        var reported = await ReportAsync(session, Film, 95);

        refused.IsFailure.ShouldBeTrue("a jump to 95 % five seconds after the start was accepted");
        refused.Error.Code.ShouldBe("learning.progress-too-fast");
        reported.IsSuccess.ShouldBeTrue();
        CompletedChapters(session).ShouldBe([Film]);
    }

    [Fact]
    public async Task Text_reported_complete_at_once_is_recorded()
    {
        var session = await StartedAsync();

        var reported = await ReportAsync(session, Intro, 100);

        reported.IsSuccess.ShouldBeTrue();
        CompletedChapters(session).ShouldBe([Intro]);
    }

    [Fact]
    public async Task A_video_chapter_the_copy_holds_no_duration_for_is_a_bug()
    {
        var broken = CourseVersionId.From(Guid.CreateVersion7());
        copies.Hold(CopyOf(broken, null));
        var session = await StartedAsync(broken);
        time.Advance(FilmDuration);

        await Should.ThrowAsync<InvalidOperationException>(() => ReportAsync(session, Film, 95));
    }

    private static CourseVersionCopy CopyOf(CourseVersionId courseVersion, int? videoSeconds) =>
        CourseVersionCopies.Of(courseVersion.Value, videoSeconds, (Intro.Value, 1, ChapterKind.Text), (Handbook.Value, 2, ChapterKind.Pdf), (Film.Value, 3, ChapterKind.Video));

    private ReportProgressHandler Handler() => new(sessions, sessions, copies, time);

    /// <summary>A session whose every chapter the learner completed through the use case, so its first ContentCompletedV1 is published.</summary>
    private async Task<LearningSessionId> CompletedAsync()
    {
        var session = await StartedAsync();
        await ReportAsync(session, Intro, 100);
        await ReportAsync(session, Handbook, 100);
        time.Advance(FilmDuration);
        await ReportAsync(session, Film, 95);
        sessions.Published.ShouldHaveSingleItem();
        return session;
    }

    private Task<Result<Unit, ApiError>> ReportAsync(LearningSessionId session, ChapterId chapter, int percent) =>
        ReportAsync(session, learner, chapter, percent);

    private Task<Result<Unit, ApiError>> ReportAsync(LearningSessionId session, PersonId by, ChapterId chapter, int percent) =>
        Handler().HandleAsync(new ReportProgress(session, by, chapter, ProgressPercent.From(percent)), TestContext.Current.CancellationToken);

    /// <summary>The learner's first run of the version, with one chapter of each kind, stored as starting it stores it; the steps start counting after it.</summary>
    private Task<LearningSessionId> StartedAsync() => StartedAsync(version);

    private async Task<LearningSessionId> StartedAsync(CourseVersionId courseVersion)
    {
        var id = LearningSessionId.For(learner, courseVersion, SessionRun.First);
        var session = LearningSession.Start(id, learner, course, courseVersion, [Intro], [Handbook], [Film], Now());
        await sessions.SaveAsync(session, TestContext.Current.CancellationToken);
        sessions.Steps.Clear();
        return id;
    }

    /// <summary>Stores what another use case would have done to the session (finishing it, reopening chapters), without announcing anything.</summary>
    private async Task ChangeAsync(LearningSessionId id, Action<LearningSession> change)
    {
        var session = (await sessions.LoadAsync<LearningSession>(id, TestContext.Current.CancellationToken)).Value;
        change(session);
        await sessions.SaveAsync(session, TestContext.Current.CancellationToken);
        sessions.Steps.Clear();
    }

    private List<ChapterId> CompletedChapters(LearningSessionId session) =>
        [.. sessions.Streams[session.Value].OfType<ChapterCompleted>().Select(completed => completed.Chapter)];

    private Timestamp Now() => Timestamp.From(time.GetUtcNow());
}
