using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Commands.ReportProgress;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Sessions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Consumers;

/// <summary>
/// Both of Assessment's outcomes arrive on Learning's one queue and are handled in parallel, so in either order: a passed
/// test outranks a lock, and a finished session takes no more progress (#42, ADR 0025).
/// </summary>
public class TestOutcomeOrderTests
{
    private readonly OutcomeSessions sessions = new();

    [Fact]
    public async Task A_lock_after_passing_changes_nothing()
    {
        var session = await sessions.CompletedAsync();
        await PassAsync(session);
        var stored = sessions.Stream(session).Count;
        var saves = sessions.Store.Saves;

        await ExhaustAsync(session, sessions.Handbook);

        sessions.Stream(session).Count.ShouldBe(stored);
        sessions.Store.Saves.ShouldBe(saves);
        var reloaded = await sessions.LoadAsync(session);
        reloaded.Status.ShouldBe(SessionStatus.Finished);
        reloaded.CompletedChapters.ShouldBe([sessions.Intro, sessions.Handbook, sessions.Film], ignoreOrder: true);
    }

    [Fact]
    public async Task A_pass_after_a_lock_finishes_the_session_with_the_reopened_chapters_left_open()
    {
        var session = await sessions.CompletedAsync();
        await ExhaustAsync(session, sessions.Handbook);

        await PassAsync(session);

        sessions.Stream(session).OfType<ChaptersReopened>().ShouldHaveSingleItem();
        sessions.Stream(session).OfType<SessionFinished>().ShouldHaveSingleItem();
        var reloaded = await sessions.LoadAsync(session);
        reloaded.Status.ShouldBe(SessionStatus.Finished);
        reloaded.CompletedChapters.ShouldBe([sessions.Intro, sessions.Film], ignoreOrder: true);
    }

    [Fact]
    public async Task A_progress_report_after_the_pass_is_refused_as_finished()
    {
        var session = await sessions.CompletedAsync();
        await PassAsync(session);
        var stored = sessions.Stream(session).Count;

        // The learner is still an active member, so the refusal comes from the finished session, not the membership check.
        var memberships = new InMemoryMembershipCopies();
        memberships.Hold(MembershipCopyRows.Started(sessions.Learner));
        var reported = await new ReportProgressHandler(memberships, sessions.Store, sessions.Store, new InMemoryAnnouncedEvents(sessions.Store.Steps), new InMemoryCourseVersionCopies(), sessions.Time).HandleAsync(
            new ReportProgress(session, sessions.Learner, sessions.Film, ProgressPercent.Complete),
            TestContext.Current.CancellationToken);

        reported.IsFailure.ShouldBeTrue("a progress report on a passed session was accepted");
        reported.Error.Code.ShouldBe("learning.session-finished");
        reported.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        sessions.Stream(session).Count.ShouldBe(stored);
    }

    private Task PassAsync(LearningSessionId session) =>
        new TestPassedV1Consumer(sessions.Store).HandleAsync(sessions.Passed(session), TestContext.Current.CancellationToken);

    private Task ExhaustAsync(LearningSessionId session, params ChapterId[] failed) =>
        new AttemptsExhaustedV1Consumer(sessions.Store, sessions.Time).HandleAsync(sessions.Exhausted(session, failed), TestContext.Current.CancellationToken);
}
