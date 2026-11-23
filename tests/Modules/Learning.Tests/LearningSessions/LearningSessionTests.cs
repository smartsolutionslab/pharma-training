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
            .Error.ShouldBeOfType<ChapterNotInCourse>();
    }

    [Fact]
    public void Exhausted_attempts_reopen_only_the_chapters_of_the_wrong_answers()
    {
        var session = Start();
        CompleteAll(session);

        session.ReopenChapters([Handbook], Now);

        session.Status.ShouldBe(SessionStatus.InProgress);
        session.CompletedChapters.ShouldBe([Intro, Film], ignoreOrder: true);
    }

    [Fact]
    public void Working_through_the_reopened_chapters_completes_the_content_again()
    {
        var session = Start();
        CompleteAll(session);
        session.ReopenChapters([Handbook], Now);

        session.RecordProgress(Handbook, ProgressPercent.Complete, Now);

        session.Status.ShouldBe(SessionStatus.ContentCompleted);
        Pending(session).OfType<ContentCompleted>().Count().ShouldBe(2);
    }

    [Fact]
    public void A_redelivered_lock_reopens_nothing_twice()
    {
        var session = Start();
        CompleteAll(session);
        session.ReopenChapters([Handbook], Now);
        var stored = Pending(session).Count;

        session.ReopenChapters([Handbook], Now);

        Pending(session).Count.ShouldBe(stored);
    }

    [Fact]
    public void A_finished_session_accepts_no_progress_and_ignores_a_late_lock()
    {
        var session = Start();
        CompleteAll(session);
        session.Finish(Now);

        session.RecordProgress(Intro, ProgressPercent.Complete, Now).Error.ShouldBeOfType<SessionFinishedAlready>();
        session.ReopenChapters([Intro], Now).IsSuccess.ShouldBeTrue();
        session.Status.ShouldBe(SessionStatus.Finished);
    }

    [Fact]
    public void Replaying_the_stored_events_rebuilds_the_same_session()
    {
        var original = Start();
        CompleteAll(original);
        original.ReopenChapters([Film], Now);

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
