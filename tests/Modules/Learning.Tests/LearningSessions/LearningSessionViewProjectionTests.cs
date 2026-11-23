using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.LearningSessions;

public class LearningSessionViewProjectionTests
{
    private static readonly Timestamp Now = Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private static readonly ChapterId Intro = ChapterId.From(Guid.CreateVersion7());
    private static readonly ChapterId Handbook = ChapterId.From(Guid.CreateVersion7());
    private static readonly ChapterId Film = ChapterId.From(Guid.CreateVersion7());

    [Fact]
    public void A_started_session_is_viewed_in_progress_with_every_chapter_at_0()
    {
        var id = LearningSessionId.New();
        var learner = PersonId.From(Guid.CreateVersion7());
        var course = CourseId.From(Guid.CreateVersion7());
        var version = CourseVersionId.From(Guid.CreateVersion7());
        var session = LearningSession.Start(id, learner, course, version, [Intro], [Handbook], [Film], Now);

        var view = LearningSessionViews.Project(Pending(session));

        view.Id.ShouldBe(id.Value);
        view.LearnerId.ShouldBe(learner.Value);
        view.CourseId.ShouldBe(course.Value);
        view.CourseVersionId.ShouldBe(version.Value);
        view.Status.ShouldBe(SessionStatus.InProgress);
        view.StartedAt.ShouldBe(Now.Value);
        view.Chapters
            .Select(chapter => (chapter.ChapterId, chapter.Kind, chapter.ProgressPercent, chapter.Completed))
            .ShouldBe(
            [
                (Intro.Value, ChapterKind.Text, 0, false),
                (Handbook.Value, ChapterKind.Pdf, 0, false),
                (Film.Value, ChapterKind.Video, 0, false),
            ],
            ignoreOrder: true);
    }

    [Fact]
    public void The_view_follows_progress_completion_reopening_and_finishing()
    {
        var session = Start();

        CompleteAll(session);
        ShouldMatch(LearningSessionViews.Project(Pending(session)), session, (Intro, 100), (Handbook, 100), (Film, 100));

        session.ReopenChapters(ExhaustionNotice.From(Guid.CreateVersion7()), [Film], Now);
        ShouldMatch(LearningSessionViews.Project(Pending(session)), session, (Intro, 100), (Handbook, 100), (Film, 0));

        session.RecordProgress(Film, ProgressPercent.Complete, Now);
        session.Finish(Now);
        ShouldMatch(LearningSessionViews.Project(Pending(session)), session, (Intro, 100), (Handbook, 100), (Film, 100));
    }

    [Fact]
    public void A_noted_lock_leaves_the_view_as_the_reopen_left_it()
    {
        var session = Start();
        CompleteAll(session);
        session.ReopenChapters(ExhaustionNotice.From(Guid.CreateVersion7()), [Film], Now);
        List<IDomainEvent> reopened = [.. Pending(session).Where(domainEvent => domainEvent is not ExhaustionNoted)];
        var before = LearningSessionViews.Project(reopened);

        var after = LearningSessionViews.Project([.. reopened, new ExhaustionNoted(session.Id, ExhaustionNotice.From(Guid.CreateVersion7()), Now)]);

        after.Status.ShouldBe(before.Status);
        after.Chapters.Select(chapter => (chapter.ChapterId, chapter.ProgressPercent, chapter.Completed))
            .ShouldBe(before.Chapters.Select(chapter => (chapter.ChapterId, chapter.ProgressPercent, chapter.Completed)), ignoreOrder: true);
    }

    private static void ShouldMatch(LearningSessionView view, LearningSession session, params (ChapterId Chapter, int Progress)[] progress)
    {
        view.Status.ShouldBe(session.Status);
        view.Chapters.Where(chapter => chapter.Completed).Select(chapter => chapter.ChapterId)
            .ShouldBe(session.CompletedChapters.Select(chapter => chapter.Value), ignoreOrder: true);
        view.Chapters.Select(chapter => (chapter.ChapterId, chapter.ProgressPercent))
            .ShouldBe(progress.Select(expected => (expected.Chapter.Value, expected.Progress)), ignoreOrder: true);
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
}
