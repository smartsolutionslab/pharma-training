using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Contracts.Assessment;
using SmartSolutionsLab.PharmaTraining.Contracts.Learning;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Progress;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;

/// <summary>
/// A learner's progress reaching Compliance (#32): a started session, three failed attempts and the content worked through
/// again apply to every active assignment of the person and course, in every pharmacy, and the latest lock or unlock
/// wins whatever order they arrive in (ADR 0023). Each event happens some hours after the assignments were assigned.
/// </summary>
public class LearningProgressConsumerTests
{
    private static readonly DateTimeOffset Monday = new(2027, 3, 1, 9, 0, 0, TimeSpan.FromHours(1));
    private static readonly PersonId Manager = PersonId.From(Guid.CreateVersion7());

    private readonly InMemoryAssignmentStore store = new();
    private readonly InMemoryLearnerProgressStore progress = new();

    // Compliance's clock reads a week after Monday, later than every event below happened, unless a test moves it.
    private readonly FakeTimeProvider time = new(Monday.AddDays(7));
    private readonly Guid anna = Guid.CreateVersion7();
    private readonly Guid hygiene = Guid.CreateVersion7();

    [Fact]
    public async Task A_start_puts_every_active_assignment_of_the_person_and_course_in_progress_in_every_pharmacy()
    {
        var inOnePharmacy = Assign(anna, hygiene);
        var inAnother = Assign(anna, hygiene);
        var otherCourse = Assign(anna, Guid.CreateVersion7());
        var otherPerson = Assign(Guid.CreateVersion7(), hygiene);
        var completed = Assign(anna, hygiene);
        completed.Complete(CertificateId.From(Guid.CreateVersion7()), Timestamp.From(At(1))).IsSuccess.ShouldBeTrue();
        var cancelled = Assign(anna, hygiene);
        cancelled.Cancel(Manager, ChangeReason.MembershipEnded, Timestamp.From(At(1))).IsSuccess.ShouldBeTrue();

        await DeliverAsync(LearningEvents.Started(anna, hygiene, At(2)));

        inOnePharmacy.Status.ShouldBe(AssignmentStatus.InProgress);
        inOnePharmacy.StartedAt.ShouldBe(Timestamp.From(At(2)));
        inAnother.Status.ShouldBe(AssignmentStatus.InProgress);
        inAnother.StartedAt.ShouldBe(Timestamp.From(At(2)));
        otherCourse.Status.ShouldBe(AssignmentStatus.Open);
        otherPerson.Status.ShouldBe(AssignmentStatus.Open);
        completed.Status.ShouldBe(AssignmentStatus.Completed);
        completed.StartedAt.ShouldBeNull();
        cancelled.Status.ShouldBe(AssignmentStatus.Cancelled);
        cancelled.StartedAt.ShouldBeNull();
        store.Commits.ShouldBe(1);
    }

    [Theory]
    [InlineData("ContentCompleted")]
    [InlineData("Exhausted")]
    public async Task Progress_of_a_person_without_an_active_assignment_changes_nothing_and_commits_nothing(string kind)
    {
        var someoneElse = Assign(Guid.CreateVersion7(), hygiene);

        await DeliverAsync(Event(kind, anna, hygiene, At(2)));

        someoneElse.Status.ShouldBe(AssignmentStatus.Open);
        someoneElse.TestAccess.ShouldBe(TestAccess.Available);
        someoneElse.TestAccessChangedAt.ShouldBeNull();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task Exhausted_attempts_lock_the_assignment()
    {
        var assignment = Assign(anna, hygiene);
        await DeliverAsync(LearningEvents.Started(anna, hygiene, At(1)));

        await DeliverAsync(LearningEvents.Exhausted(anna, hygiene, At(2)));

        assignment.TestAccess.ShouldBe(TestAccess.LockedAfterFailedAttempts);
        assignment.TestAccessChangedAt.ShouldBe(Timestamp.From(At(2)));
        store.Commits.ShouldBe(2);
    }

    [Fact]
    public async Task Content_completed_later_unlocks_the_assignment()
    {
        var assignment = Assign(anna, hygiene);
        await DeliverAsync(LearningEvents.Started(anna, hygiene, At(1)));
        await DeliverAsync(LearningEvents.Exhausted(anna, hygiene, At(2)));

        await DeliverAsync(LearningEvents.ContentCompleted(anna, hygiene, At(3)));

        assignment.TestAccess.ShouldBe(TestAccess.Available);
        assignment.TestAccessChangedAt.ShouldBe(Timestamp.From(At(3)));
        assignment.Status.ShouldBe(AssignmentStatus.InProgress);
    }

    [Fact]
    public async Task Content_completed_of_the_same_cycle_delivered_after_its_lock_leaves_it_locked()
    {
        var assignment = Assign(anna, hygiene);
        await DeliverAsync(LearningEvents.Started(anna, hygiene, At(1)));
        var completed = LearningEvents.ContentCompleted(anna, hygiene, At(2));
        await DeliverAsync(LearningEvents.Exhausted(anna, hygiene, At(3)));

        await DeliverAsync(completed);

        assignment.TestAccess.ShouldBe(TestAccess.LockedAfterFailedAttempts);
        assignment.TestAccessChangedAt.ShouldBe(Timestamp.From(At(3)));
    }

    [Fact]
    public async Task Content_completed_of_the_next_cycle_overtaking_the_lock_ends_unlocked()
    {
        var assignment = Assign(anna, hygiene);
        await DeliverAsync(LearningEvents.Started(anna, hygiene, At(1)));

        await DeliverAsync(LearningEvents.ContentCompleted(anna, hygiene, At(2)));
        await DeliverAsync(LearningEvents.ContentCompleted(anna, hygiene, At(4)));
        await DeliverAsync(LearningEvents.Exhausted(anna, hygiene, At(3)));

        assignment.TestAccess.ShouldBe(TestAccess.Available);
        assignment.TestAccessChangedAt.ShouldBe(Timestamp.From(At(4)));
    }

    [Fact]
    public async Task Each_event_delivered_twice_changes_nothing()
    {
        var assignment = Assign(anna, hygiene);
        var started = LearningEvents.Started(anna, hygiene, At(1));
        var exhausted = LearningEvents.Exhausted(anna, hygiene, At(2));
        var completed = LearningEvents.ContentCompleted(anna, hygiene, At(3));

        await DeliverAsync(started);
        await DeliverAsync(started);
        assignment.Status.ShouldBe(AssignmentStatus.InProgress);
        assignment.StartedAt.ShouldBe(Timestamp.From(At(1)));

        await DeliverAsync(exhausted);
        await DeliverAsync(exhausted);
        assignment.TestAccess.ShouldBe(TestAccess.LockedAfterFailedAttempts);
        assignment.TestAccessChangedAt.ShouldBe(Timestamp.From(At(2)));

        await DeliverAsync(completed);
        await DeliverAsync(completed);
        assignment.TestAccess.ShouldBe(TestAccess.Available);
        assignment.TestAccessChangedAt.ShouldBe(Timestamp.From(At(3)));

        await DeliverAsync(exhausted);
        assignment.TestAccess.ShouldBe(TestAccess.Available);
        assignment.TestAccessChangedAt.ShouldBe(Timestamp.From(At(3)));
        assignment.StartedAt.ShouldBe(Timestamp.From(At(1)));
    }

    [Theory]
    [InlineData(nameof(LearningSessionStartedV1.SessionId))]
    [InlineData(nameof(LearningSessionStartedV1.PersonId))]
    [InlineData(nameof(LearningSessionStartedV1.CourseId))]
    [InlineData(nameof(LearningSessionStartedV1.CourseVersionId))]
    public async Task A_start_with_an_empty_id_is_refused_by_its_name_and_nothing_changed(string field)
    {
        var assignment = Assign(anna, hygiene);
        var started = LearningEvents.Started(anna, hygiene, At(1));
        started = started with
        {
            SessionId = Emptied(field, nameof(started.SessionId), started.SessionId),
            PersonId = Emptied(field, nameof(started.PersonId), started.PersonId),
            CourseId = Emptied(field, nameof(started.CourseId), started.CourseId),
            CourseVersionId = Emptied(field, nameof(started.CourseVersionId), started.CourseVersionId),
        };

        var refused = await Should.ThrowAsync<GuardException>(() => new LearningSessionStartedV1Consumer(store, progress, store, time).HandleAsync(started, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(field);
        assignment.Status.ShouldBe(AssignmentStatus.Open);
        store.Commits.ShouldBe(0);
    }

    [Theory]
    [InlineData(nameof(ContentCompletedV1.SessionId))]
    [InlineData(nameof(ContentCompletedV1.PersonId))]
    [InlineData(nameof(ContentCompletedV1.CourseId))]
    [InlineData(nameof(ContentCompletedV1.CourseVersionId))]
    public async Task Content_completed_with_an_empty_id_is_refused_by_its_name_and_nothing_changed(string field)
    {
        var assignment = Assign(anna, hygiene);
        assignment.MarkStarted(Timestamp.From(At(1)));
        assignment.MarkLocked(Timestamp.From(At(2)));
        var completed = LearningEvents.ContentCompleted(anna, hygiene, At(3));
        completed = completed with
        {
            SessionId = Emptied(field, nameof(completed.SessionId), completed.SessionId),
            PersonId = Emptied(field, nameof(completed.PersonId), completed.PersonId),
            CourseId = Emptied(field, nameof(completed.CourseId), completed.CourseId),
            CourseVersionId = Emptied(field, nameof(completed.CourseVersionId), completed.CourseVersionId),
        };

        var refused = await Should.ThrowAsync<GuardException>(() => new ContentCompletedV1Consumer(store, store, time).HandleAsync(completed, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(field);
        assignment.TestAccess.ShouldBe(TestAccess.LockedAfterFailedAttempts);
        store.Commits.ShouldBe(0);
    }

    [Theory]
    [InlineData(nameof(AttemptsExhaustedV1.SessionId))]
    [InlineData(nameof(AttemptsExhaustedV1.PersonId))]
    [InlineData(nameof(AttemptsExhaustedV1.CourseId))]
    [InlineData(nameof(AttemptsExhaustedV1.CourseVersionId))]
    public async Task Exhausted_attempts_with_an_empty_id_are_refused_by_their_name_and_nothing_changed(string field)
    {
        var assignment = Assign(anna, hygiene);
        assignment.MarkStarted(Timestamp.From(At(1)));
        var exhausted = LearningEvents.Exhausted(anna, hygiene, At(2));
        exhausted = exhausted with
        {
            SessionId = Emptied(field, nameof(exhausted.SessionId), exhausted.SessionId),
            PersonId = Emptied(field, nameof(exhausted.PersonId), exhausted.PersonId),
            CourseId = Emptied(field, nameof(exhausted.CourseId), exhausted.CourseId),
            CourseVersionId = Emptied(field, nameof(exhausted.CourseVersionId), exhausted.CourseVersionId),
        };

        var refused = await Should.ThrowAsync<GuardException>(() => new AttemptsExhaustedV1Consumer(store, store, time).HandleAsync(exhausted, TestContext.Current.CancellationToken));

        refused.ParamName.ShouldBe(field);
        assignment.TestAccess.ShouldBe(TestAccess.Available);
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task No_message_is_refused_by_each_consumer_and_nothing_committed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await Should.ThrowAsync<GuardException>(() => new LearningSessionStartedV1Consumer(store, progress, store, time).HandleAsync(null!, cancellationToken));
        await Should.ThrowAsync<GuardException>(() => new ContentCompletedV1Consumer(store, store, time).HandleAsync(null!, cancellationToken));
        await Should.ThrowAsync<GuardException>(() => new AttemptsExhaustedV1Consumer(store, store, time).HandleAsync(null!, cancellationToken));

        store.Commits.ShouldBe(0);
    }

    /// <summary>
    /// A publisher's clock ahead of Compliance's must not hold off what happens next: an event stamped in the future counts
    /// as having happened when Compliance received it, so a later unlock still wins (follow-up to #247 review).
    /// </summary>
    [Fact]
    public async Task A_lock_stamped_in_the_future_counts_as_received_and_a_later_unlock_still_wins()
    {
        var assignment = Assign(anna, hygiene);
        await DeliverAsync(LearningEvents.Started(anna, hygiene, At(2)));
        var received = time.GetUtcNow();

        await DeliverAsync(LearningEvents.Exhausted(anna, hygiene, received.AddDays(30)));

        assignment.TestAccess.ShouldBe(TestAccess.LockedAfterFailedAttempts);
        assignment.TestAccessChangedAt.ShouldBe(Timestamp.From(received));

        time.Advance(TimeSpan.FromHours(1));
        await DeliverAsync(LearningEvents.ContentCompleted(anna, hygiene, received.AddHours(1)));

        assignment.TestAccess.ShouldBe(TestAccess.Available);
        assignment.TestAccessChangedAt.ShouldBe(Timestamp.From(received.AddHours(1)));
    }

    [Fact]
    public async Task A_start_stamped_in_the_future_counts_as_received()
    {
        var assignment = Assign(anna, hygiene);
        var received = time.GetUtcNow();

        await DeliverAsync(LearningEvents.Started(anna, hygiene, received.AddDays(30)));

        assignment.Status.ShouldBe(AssignmentStatus.InProgress);
        assignment.StartedAt.ShouldBe(Timestamp.From(received));
    }

    private static DateTimeOffset At(int hoursAfterMonday) => Monday.AddHours(hoursAfterMonday);

    private static Guid Emptied(string field, string name, Guid value) => field == name ? Guid.Empty : value;

    private static IntegrationEvent Event(string kind, Guid personId, Guid courseId, DateTimeOffset occurredAt) =>
        kind switch
        {
            "Started" => LearningEvents.Started(personId, courseId, occurredAt),
            "ContentCompleted" => LearningEvents.ContentCompleted(personId, courseId, occurredAt),
            "Exhausted" => LearningEvents.Exhausted(personId, courseId, occurredAt),
            _ => throw new InvalidOperationException($"No progress event is called {kind}."),
        };

    /// <summary>A stored assignment of the learner and course, in a pharmacy of its own, assigned on Monday.</summary>
    private Assignment Assign(Guid learner, Guid course)
    {
        var assignment = Assignment.Assign(
            AssignmentId.New(),
            PersonId.From(learner),
            PharmacyId.From(Guid.CreateVersion7()),
            CourseId.From(course),
            DueDate.On(CalendarDate.From(new DateOnly(2027, 3, 31))),
            null,
            Manager,
            Timestamp.From(Monday)).Value;
        store.Stored.Add(assignment);
        return assignment;
    }

    private Task DeliverAsync(IntegrationEvent message)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        return message switch
        {
            LearningSessionStartedV1 started => new LearningSessionStartedV1Consumer(store, progress, store, time).HandleAsync(started, cancellationToken),
            ContentCompletedV1 completed => new ContentCompletedV1Consumer(store, store, time).HandleAsync(completed, cancellationToken),
            AttemptsExhaustedV1 exhausted => new AttemptsExhaustedV1Consumer(store, store, time).HandleAsync(exhausted, cancellationToken),
            _ => throw new InvalidOperationException($"No consumer takes {message.GetType().Name}."),
        };
    }
}
