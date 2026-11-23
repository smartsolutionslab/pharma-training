using System.Net;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Contracts.Learning;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Commands.StartLearningSession;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions.Events;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Tests.Sessions;

public class StartLearningSessionTests
{
    private static readonly Guid Intro = Guid.CreateVersion7();
    private static readonly Guid Handbook = Guid.CreateVersion7();
    private static readonly Guid Film = Guid.CreateVersion7();

    private readonly InMemoryCourseVersionCopies copies = new();
    private readonly InMemoryMembershipCopies memberships = new();
    private readonly InMemoryLearningSessions sessions = new();
    private readonly FakeTimeProvider time = new(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private readonly PersonId learner = PersonId.From(Guid.CreateVersion7());
    private readonly CourseVersionCopy version;
    private readonly MemberCopy membership;

    /// <summary>The learner is an active member: only one may start or resume a session (#194).</summary>
    public StartLearningSessionTests()
    {
        version = CourseVersionCopies.Of(Guid.CreateVersion7(), (Intro, 1, ChapterKind.Text), (Handbook, 2, ChapterKind.Pdf), (Film, 3, ChapterKind.Video));
        copies.Hold(version);
        membership = MembershipCopyRows.Started(learner);
        memberships.Hold(membership);
    }

    private CourseVersionId Version => CourseVersionId.From(version.Id);

    [Fact]
    public async Task Starting_a_version_creates_a_session_with_its_chapters_by_kind()
    {
        var started = await StartAsync(learner);

        started.Value.Created.ShouldBeTrue();
        var session = await LoadAsync(started.Value.Id);
        session.Status.ShouldBe(SessionStatus.InProgress);
        session.Learner.ShouldBe(learner);
        session.CourseVersion.ShouldBe(Version);
        session.Course.ShouldBe(CourseId.From(version.CourseId));
        session.RecordProgress(ChapterId.From(Film), ProgressPercent.From(95), Timestamp.From(time.GetUtcNow()));
        session.RecordProgress(ChapterId.From(Intro), ProgressPercent.From(99), Timestamp.From(time.GetUtcNow()));
        session.CompletedChapters.ShouldBe([ChapterId.From(Film)]);
        sessions.Streams[started.Value.Id].OfType<SessionStarted>().ShouldHaveSingleItem().StartedAt.ShouldBe(Timestamp.From(time.GetUtcNow()));
    }

    [Fact]
    public async Task Starting_again_resumes_the_unfinished_session_and_stores_nothing()
    {
        var first = await StartAsync(learner);
        var saves = sessions.Saves;
        time.Advance(TimeSpan.FromHours(1));

        var again = await StartAsync(learner);

        again.Value.Id.ShouldBe(first.Value.Id);
        again.Value.Created.ShouldBeFalse();
        sessions.Saves.ShouldBe(saves);
    }

    [Fact]
    public async Task The_first_session_has_the_id_of_run_1()
    {
        var started = await StartAsync(learner);

        started.Value.Id.ShouldBe(LearningSessionId.For(learner, Version, SessionRun.First).Value);
    }

    [Fact]
    public async Task After_a_finished_session_a_start_creates_the_next_run()
    {
        var now = Timestamp.From(time.GetUtcNow());
        var finished = LearningSession.Start(
            LearningSessionId.For(learner, Version, SessionRun.First),
            learner,
            CourseId.From(version.CourseId),
            Version,
            [ChapterId.From(Intro)],
            [ChapterId.From(Handbook)],
            [ChapterId.From(Film)],
            now);
        finished.Finish(now);
        await sessions.SaveAsync(finished, TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromDays(365));

        var started = await StartAsync(learner);

        started.Value.Id.ShouldBe(LearningSessionId.For(learner, Version, SessionRun.From(2)).Value);
        started.Value.Created.ShouldBeTrue();
    }

    [Fact]
    public async Task Another_learner_on_the_same_version_gets_a_session_of_their_own()
    {
        var mine = await StartAsync(learner);
        var other = PersonId.From(Guid.CreateVersion7());
        memberships.Hold(MembershipCopyRows.Started(other));

        var theirs = await StartAsync(other);

        theirs.Value.Created.ShouldBeTrue();
        theirs.Value.Id.ShouldNotBe(mine.Value.Id);
    }

    [Fact]
    public async Task A_version_Learning_does_not_hold_is_not_found()
    {
        var started = await Handler().HandleAsync(
            new StartLearningSession(learner, CourseVersionId.From(Guid.CreateVersion7())),
            TestContext.Current.CancellationToken);

        started.Error.Code.ShouldBe("learning.course-version-not-found");
        started.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        sessions.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task A_person_without_a_membership_may_not_start_and_nothing_is_stored()
    {
        var started = await StartAsync(PersonId.From(Guid.CreateVersion7()));

        ShouldBeRefusedAsNonMember(started);
        sessions.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task A_person_whose_membership_ended_may_not_start()
    {
        memberships.Hold(MembershipCopyRows.EndingOf(membership));

        var started = await StartAsync(learner);

        ShouldBeRefusedAsNonMember(started);
        sessions.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task A_new_session_on_a_retired_version_is_refused_with_409_and_nothing_stored()
    {
        Retire(version.Id, version.CourseId);

        var started = await StartAsync(learner);

        started.Error.Code.ShouldBe("learning.course-version-retired");
        started.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        sessions.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task A_person_whose_membership_ended_may_not_resume_their_unfinished_session()
    {
        var first = await StartAsync(learner);
        first.Value.Created.ShouldBeTrue();
        var saves = sessions.Saves;
        memberships.Hold(MembershipCopyRows.EndingOf(membership));

        var again = await StartAsync(learner);

        ShouldBeRefusedAsNonMember(again);
        sessions.Saves.ShouldBe(saves);
    }

    [Fact]
    public async Task An_unfinished_session_on_a_retired_version_is_resumed_and_nothing_stored()
    {
        var first = await StartAsync(learner);
        Retire(version.Id, version.CourseId);
        time.Advance(TimeSpan.FromHours(1));

        var again = await StartAsync(learner);

        again.Value.Created.ShouldBeFalse();
        again.Value.Id.ShouldBe(first.Value.Id);
        sessions.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task After_a_finished_session_on_a_retired_version_a_new_run_is_refused()
    {
        var now = Timestamp.From(time.GetUtcNow());
        var finished = LearningSession.Start(
            LearningSessionId.For(learner, Version, SessionRun.First),
            learner,
            CourseId.From(version.CourseId),
            Version,
            [ChapterId.From(Intro)],
            [ChapterId.From(Handbook)],
            [ChapterId.From(Film)],
            now);
        finished.Finish(now);
        await sessions.SaveAsync(finished, TestContext.Current.CancellationToken);
        var saves = sessions.Saves;
        Retire(version.Id, version.CourseId);
        time.Advance(TimeSpan.FromDays(365));

        var started = await StartAsync(learner);

        started.Error.Code.ShouldBe("learning.course-version-retired");
        started.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        sessions.Saves.ShouldBe(saves);
    }

    [Fact]
    public async Task A_non_member_starting_a_version_learning_does_not_hold_is_refused_as_a_non_member()
    {
        var started = await Handler().HandleAsync(
            new StartLearningSession(PersonId.From(Guid.CreateVersion7()), CourseVersionId.From(Guid.CreateVersion7())),
            TestContext.Current.CancellationToken);

        ShouldBeRefusedAsNonMember(started);
        sessions.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task A_retirement_held_before_its_version_refuses_the_start_once_the_version_arrives()
    {
        var later = CourseVersionCopies.Of(Guid.CreateVersion7(), (Intro, 1, ChapterKind.Text));
        Retire(later.Id, later.CourseId);
        copies.Hold(later);

        var started = await Handler().HandleAsync(
            new StartLearningSession(learner, CourseVersionId.From(later.Id)),
            TestContext.Current.CancellationToken);

        started.Error.Code.ShouldBe("learning.course-version-retired");
        started.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        sessions.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task A_member_of_any_pharmacy_may_start_although_another_membership_ended()
    {
        var moved = PersonId.From(Guid.CreateVersion7());
        var left = MembershipCopyRows.Started(moved);
        memberships.Hold(left);
        memberships.Hold(MembershipCopyRows.EndingOf(left));
        memberships.Hold(MembershipCopyRows.Started(moved));

        var started = await StartAsync(moved);

        started.IsSuccess.ShouldBeTrue();
        started.Value.Created.ShouldBeTrue();
    }

    [Fact]
    public async Task A_retired_version_Learning_does_not_hold_is_not_found()
    {
        var unknown = Guid.CreateVersion7();
        Retire(unknown, Guid.CreateVersion7());

        var started = await Handler().HandleAsync(
            new StartLearningSession(learner, CourseVersionId.From(unknown)),
            TestContext.Current.CancellationToken);

        started.Error.Code.ShouldBe("learning.course-version-not-found");
        started.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        sessions.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Starting_a_version_announces_the_session_once_with_its_ids()
    {
        var started = await StartAsync(learner);

        var announced = sessions.Published.ShouldHaveSingleItem().ShouldBeOfType<LearningSessionStartedV1>();
        announced.SessionId.ShouldBe(started.Value.Id);
        announced.PersonId.ShouldBe(learner.Value);
        announced.CourseId.ShouldBe(version.CourseId);
        announced.CourseVersionId.ShouldBe(version.Id);
        sessions.Commits.ShouldBe(1);
        sessions.Steps.ShouldBe([InMemoryLearningSessions.Saved, InMemoryLearningSessions.Queued, InMemoryLearningSessions.Committed]);
    }

    [Fact]
    public async Task Resuming_a_session_announces_nothing()
    {
        await StartAsync(learner);
        var steps = sessions.Steps.Count;
        time.Advance(TimeSpan.FromHours(1));

        var again = await StartAsync(learner);

        again.Value.Created.ShouldBeFalse();
        sessions.Published.Count.ShouldBe(1);
        sessions.Commits.ShouldBe(1);
        sessions.Steps.Count.ShouldBe(steps);
    }

    [Fact]
    public async Task A_version_Learning_does_not_hold_announces_nothing()
    {
        var started = await Handler().HandleAsync(
            new StartLearningSession(learner, CourseVersionId.From(Guid.CreateVersion7())),
            TestContext.Current.CancellationToken);

        started.IsFailure.ShouldBeTrue();
        sessions.Published.ShouldBeEmpty();
        sessions.Commits.ShouldBe(0);
        sessions.Steps.ShouldBeEmpty();
    }

    [Fact]
    public async Task After_a_finished_session_the_next_run_is_announced_under_its_own_id()
    {
        var now = Timestamp.From(time.GetUtcNow());
        var finished = LearningSession.Start(
            LearningSessionId.For(learner, Version, SessionRun.First),
            learner,
            CourseId.From(version.CourseId),
            Version,
            [ChapterId.From(Intro)],
            [ChapterId.From(Handbook)],
            [ChapterId.From(Film)],
            now);
        finished.Finish(now);
        await sessions.SaveAsync(finished, TestContext.Current.CancellationToken);
        time.Advance(TimeSpan.FromDays(365));

        var started = await StartAsync(learner);

        var announced = sessions.Published.ShouldHaveSingleItem().ShouldBeOfType<LearningSessionStartedV1>();
        announced.SessionId.ShouldBe(LearningSessionId.For(learner, Version, SessionRun.From(2)).Value);
        announced.SessionId.ShouldBe(started.Value.Id);
        announced.PersonId.ShouldBe(learner.Value);
        announced.CourseVersionId.ShouldBe(version.Id);
        sessions.Commits.ShouldBe(1);
    }

    /// <summary>A resume never announces the session again, so a start whose caller goes away once it is stored still commits its announcement.</summary>
    [Fact]
    public async Task A_new_session_is_announced_even_when_the_caller_goes_away_after_the_save()
    {
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        sessions.CancelAfterNextSave(caller);

        var started = await Handler().HandleAsync(new StartLearningSession(learner, Version), caller.Token);

        sessions.Published.ShouldHaveSingleItem().ShouldBeOfType<LearningSessionStartedV1>().SessionId.ShouldBe(started.Value.Id);
        sessions.Commits.ShouldBe(1);
    }

    /// <summary>Of two first starts of one version, the loser's save is refused (Marten's stream collision); it announces nothing.</summary>
    [Fact]
    public async Task A_start_that_loses_the_race_announces_nothing()
    {
        sessions.LoseNextSave();

        await Should.ThrowAsync<InvalidOperationException>(() => StartAsync(learner));

        sessions.Published.ShouldBeEmpty();
        sessions.Commits.ShouldBe(0);
        sessions.Steps.ShouldBeEmpty();
    }

    private static void ShouldBeRefusedAsNonMember(Result<LearningSessionStart, ApiError> started)
    {
        started.IsSuccess.ShouldBeFalse("the handler let a person who is no active member start or resume a session");
        started.Error.Code.ShouldBe("learning.membership-missing");
        started.Error.Status.ShouldBe(HttpStatusCode.Forbidden);
    }

    private StartLearningSessionHandler Handler() => new(copies, memberships, sessions, sessions, sessions, time);

    private Task<Result<LearningSessionStart, ApiError>> StartAsync(PersonId person) =>
        Handler().HandleAsync(new StartLearningSession(person, Version), TestContext.Current.CancellationToken);

    private void Retire(Guid courseVersionId, Guid courseId) =>
        copies.Retire(new CourseVersionRetirement { CourseVersionId = courseVersionId, CourseId = courseId });

    private async Task<LearningSession> LoadAsync(Guid id) =>
        (await sessions.LoadAsync<LearningSession>(LearningSessionId.From(id), TestContext.Current.CancellationToken)).Value;
}
