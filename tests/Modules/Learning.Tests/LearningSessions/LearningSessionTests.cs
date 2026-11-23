// Learning — behaviour of the event-sourced LearningSession aggregate.

using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.LearningSessions;

public class LearningSessionTests
{
    private static readonly Timestamp Now = Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private static readonly ChapterId Intro = ChapterId.From(Guid.CreateVersion7());
    private static readonly ChapterId Handbook = ChapterId.From(Guid.CreateVersion7());
    private static readonly ChapterId Film = ChapterId.From(Guid.CreateVersion7());

    [Fact]
    public void A_new_session_has_every_chapter_open()
    {
        var session = Start();

        session.Status.ShouldBe(SessionStatus.InProgress);
        session.CompletedChapters.ShouldBeEmpty();
    }

    [Fact]
    public void A_text_chapter_counts_only_when_read_to_the_end()
    {
        var session = Start();

        session.RecordProgress(Intro, ProgressPercent.From(99), Now);
        session.CompletedChapters.ShouldBeEmpty();

        session.RecordProgress(Intro, ProgressPercent.Complete, Now);
        session.CompletedChapters.ShouldBe([Intro]);
    }

    [Fact]
    public void A_video_counts_from_95_percent()
    {
        var session = Start();

        session.RecordProgress(Film, ProgressPercent.From(95), Now);

        session.CompletedChapters.ShouldContain(Film);
    }

    [Fact]
    public void Older_or_repeated_progress_stores_nothing()
    {
        var session = Start();
        session.RecordProgress(Film, ProgressPercent.From(60), Now);
        var stored = Pending(session).Count;

        session.RecordProgress(Film, ProgressPercent.From(40), Now);
        session.RecordProgress(Film, ProgressPercent.From(60), Now);

        Pending(session).Count.ShouldBe(stored);
    }

    [Fact]
    public void Working_through_every_chapter_completes_the_content_once()
    {
        var session = Start();

        CompleteAll(session);
        session.RecordProgress(Intro, ProgressPercent.Complete, Now); // a late duplicate

        session.Status.ShouldBe(SessionStatus.ContentCompleted);
        Pending(session).OfType<ContentCompleted>().ShouldHaveSingleItem();
    }

    [Fact]
    public void A_chapter_of_another_version_is_refused()
    {
        var session = Start();

        session.RecordProgress(ChapterId.From(Guid.CreateVersion7()), ProgressPercent.Complete, Now)
            .Error.Value.ShouldBeOfType<ChapterNotInCourse>();
    }

    [Fact]
    public void Exhausted_attempts_reopen_only_the_chapters_of_the_wrong_answers()
    {
        var session = Start();
        CompleteAll(session);

        session.ReopenChapters(Notice(), [Handbook], Now);

        session.Status.ShouldBe(SessionStatus.InProgress);
        session.CompletedChapters.ShouldBe([Intro, Film], ignoreOrder: true);
    }

    [Fact]
    public void Working_through_the_reopened_chapters_completes_the_content_again()
    {
        var session = Start();
        CompleteAll(session);
        session.ReopenChapters(Notice(), [Handbook], Now);

        session.RecordProgress(Handbook, ProgressPercent.Complete, Now);

        session.Status.ShouldBe(SessionStatus.ContentCompleted);
        Pending(session).OfType<ContentCompleted>().Count().ShouldBe(2);
    }

    [Fact]
    public void A_redelivered_lock_reopens_nothing_twice()
    {
        var session = Start();
        CompleteAll(session);
        var notice = Notice();
        session.ReopenChapters(notice, [Handbook], Now);
        var stored = Pending(session).Count;

        session.ReopenChapters(notice, [Handbook], Now);

        Pending(session).Count.ShouldBe(stored);
    }

    [Fact]
    public void A_finished_session_ignores_a_late_lock_and_notes_nothing()
    {
        var session = Start();
        CompleteAll(session);
        session.Finish(Now);
        var stored = Pending(session).Count;

        session.RecordProgress(Intro, ProgressPercent.Complete, Now).Error.Value.ShouldBeOfType<SessionFinishedAlready>();
        session.ReopenChapters(Notice(), [Intro], Now).IsSuccess.ShouldBeTrue();

        Pending(session).Count.ShouldBe(stored);
        session.Status.ShouldBe(SessionStatus.Finished);
        session.CompletedChapters.ShouldBe([Intro, Handbook, Film], ignoreOrder: true);
    }

    [Fact]
    public void A_lock_delivered_again_after_its_chapters_were_worked_through_reopens_nothing()
    {
        var session = Start();
        CompleteAll(session);
        var notice = Notice();
        session.ReopenChapters(notice, [Handbook], Now);
        session.RecordProgress(Handbook, ProgressPercent.Complete, Now);
        session.Status.ShouldBe(SessionStatus.ContentCompleted);
        var stored = Pending(session).Count;

        session.ReopenChapters(notice, [Handbook], Now).IsSuccess.ShouldBeTrue();

        Pending(session).Count.ShouldBe(stored, "the same lock, delivered again, reopened the chapters a second time");
        session.Status.ShouldBe(SessionStatus.ContentCompleted);
        session.CompletedChapters.ShouldBe([Intro, Handbook, Film], ignoreOrder: true);
    }

    [Fact]
    public void A_lock_reopens_its_chapters_and_notes_its_announcement_once()
    {
        var session = Start();
        CompleteAll(session);
        var stored = Pending(session).Count;
        var notice = Notice();

        session.ReopenChapters(notice, [Handbook, Handbook], Now).IsSuccess.ShouldBeTrue();

        var raised = Pending(session).Skip(stored).ToList();
        raised.Count.ShouldBe(2);
        var reopened = raised[0].ShouldBeOfType<ChaptersReopened>();
        reopened.Session.ShouldBe(session.Id);
        reopened.Chapters.ShouldBe([Handbook]);
        reopened.ReopenedAt.ShouldBe(Now);
        var noted = raised[1].ShouldBeOfType<ExhaustionNoted>();
        noted.Session.ShouldBe(session.Id);
        noted.Notice.ShouldBe(notice);
        noted.NotedAt.ShouldBe(Now);
    }

    [Fact]
    public void A_replayed_session_still_knows_the_locks_it_noted()
    {
        var original = Start();
        CompleteAll(original);
        var notice = Notice();
        original.ReopenChapters(notice, [Handbook], Now);
        original.RecordProgress(Handbook, ProgressPercent.Complete, Now);

        var replayed = Replay(Pending(original));
        replayed.ReopenChapters(notice, [Handbook], Now).IsSuccess.ShouldBeTrue();

        Pending(replayed).ShouldBeEmpty("the replayed session forgot the lock it noted and reopened its chapters again");
        replayed.Status.ShouldBe(SessionStatus.ContentCompleted);
    }

    [Fact]
    public void A_new_lock_after_the_content_was_completed_again_reopens_its_chapters()
    {
        var session = Start();
        CompleteAll(session);
        session.ReopenChapters(Notice(), [Handbook], Now);
        session.RecordProgress(Handbook, ProgressPercent.Complete, Now);

        session.ReopenChapters(Notice(), [Handbook], Now).IsSuccess.ShouldBeTrue();

        session.Status.ShouldBe(SessionStatus.InProgress);
        session.CompletedChapters.ShouldBe([Intro, Film], ignoreOrder: true);
        Pending(session).OfType<ChaptersReopened>().Count().ShouldBe(2);
    }

    [Fact]
    public void A_lock_naming_a_chapter_outside_the_session_is_refused_and_changes_nothing()
    {
        var session = Start();
        CompleteAll(session);
        var stored = Pending(session).Count;

        var reopened = session.ReopenChapters(Notice(), [Handbook, ChapterId.From(Guid.CreateVersion7())], Now);

        reopened.IsFailure.ShouldBeTrue("a lock naming a chapter the session does not have was accepted");
        reopened.Error.ShouldBeOfType<ChapterNotInCourse>().Code.ShouldBe("learning.chapter-not-in-course");
        Pending(session).Count.ShouldBe(stored);
        session.Status.ShouldBe(SessionStatus.ContentCompleted);
        session.CompletedChapters.ShouldBe([Intro, Handbook, Film], ignoreOrder: true);
    }

    [Fact]
    public void A_finished_session_ignores_even_a_lock_naming_an_outside_chapter()
    {
        var session = Start();
        CompleteAll(session);
        session.Finish(Now);
        var stored = Pending(session).Count;

        session.ReopenChapters(Notice(), [Handbook, ChapterId.From(Guid.CreateVersion7())], Now).IsSuccess.ShouldBeTrue();

        Pending(session).Count.ShouldBe(stored);
        session.Status.ShouldBe(SessionStatus.Finished);
    }

    [Fact]
    public void Replaying_the_stored_events_rebuilds_the_same_session()
    {
        var original = Start();
        CompleteAll(original);
        original.ReopenChapters(Notice(), [Film], Now);

        var replayed = Replay(Pending(original));

        replayed.Status.ShouldBe(original.Status);
        replayed.CompletedChapters.ShouldBe(original.CompletedChapters, ignoreOrder: true);
        replayed.Learner.ShouldBe(original.Learner);
    }

    private static LearningSession Start() =>
        LearningSession.Start(
            LearningSessionId.New(),
            PersonId.From(Guid.CreateVersion7()),
            CourseId.From(Guid.CreateVersion7()),
            CourseVersionId.From(Guid.CreateVersion7()),
            textChapters: [Intro],
            pdfChapters: [Handbook],
            videoChapters: [Film],
            Now);

    private static ExhaustionNotice Notice() => ExhaustionNotice.From(Guid.CreateVersion7());

    private static void CompleteAll(LearningSession session)
    {
        session.RecordProgress(Intro, ProgressPercent.Complete, Now);
        session.RecordProgress(Handbook, ProgressPercent.Complete, Now);
        session.RecordProgress(Film, ProgressPercent.Complete, Now);
    }

    private static IReadOnlyList<IDomainEvent> Pending(LearningSession session) => ((IEventSourced)session).PendingEvents;

    /// <summary>Loads a session the way the aggregate store does: the private constructor, then the history.</summary>
    private static LearningSession Replay(IReadOnlyList<IDomainEvent> history)
    {
        var session = (LearningSession)Activator.CreateInstance(typeof(LearningSession), nonPublic: true)!;
        ((IEventSourced)session).Replay(history);
        return session;
    }
}
