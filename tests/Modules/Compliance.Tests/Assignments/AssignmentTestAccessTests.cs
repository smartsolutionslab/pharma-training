using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;

/// <summary>
/// The start of a learning session, three failed attempts (a lock) and the content worked through again (an unlock), as
/// the assignment applies them (owner decision on #32, option b; ADR 0023): the test access follows whichever lock or
/// unlock happened last, the lock winning a tie, and nothing that happened before the assignment concerns it. An event
/// is written as its kind and the day after <c>Monday</c> it happened on: <c>AE2</c> is a lock on day 2, <c>CC3</c> an
/// unlock on day 3.
/// </summary>
public class AssignmentTestAccessTests
{
    private static readonly Timestamp Monday = Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.FromHours(1)));
    private static readonly PersonId Anna = PersonId.From(Guid.CreateVersion7());
    private static readonly PersonId Manager = PersonId.From(Guid.CreateVersion7());
    private static readonly PharmacyId Pharmacy = PharmacyId.From(Guid.CreateVersion7());
    private static readonly CourseId Hygiene = CourseId.From(Guid.CreateVersion7());

    public static TheoryData<string> EveryOrderOfALockBetweenTwoCompletions => Orders("CC2", "AE3", "CC4");

    public static TheoryData<string> EveryOrderOfTwoCycles => Orders("CC2", "AE3", "CC4", "AE5");

    [Fact]
    public void A_start_puts_an_open_assignment_in_progress_at_the_time_it_happened()
    {
        var assignment = Assign();

        assignment.MarkStarted(Later(1));

        assignment.Status.ShouldBe(AssignmentStatus.InProgress);
        assignment.StartedAt.ShouldBe(Later(1));
        assignment.TestAccess.ShouldBe(TestAccess.Available);
        assignment.TestAccessChangedAt.ShouldBeNull();
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    public void The_earliest_start_wins_whatever_order_the_starts_arrive_in(int first, int second)
    {
        var assignment = Assign();

        assignment.MarkStarted(Later(first));
        assignment.MarkStarted(Later(second));

        assignment.Status.ShouldBe(AssignmentStatus.InProgress);
        assignment.StartedAt.ShouldBe(Later(1));
    }

    [Fact]
    public void A_start_no_longer_unlocks_a_locked_assignment()
    {
        var assignment = Started();
        assignment.MarkLocked(Later(2));

        assignment.MarkStarted(Later(3));

        assignment.TestAccess.ShouldBe(TestAccess.LockedAfterFailedAttempts);
        assignment.TestAccessChangedAt.ShouldBe(Later(2));
        assignment.StateOn(Day(3)).ShouldBe(AssignmentState.Locked);
    }

    [Fact]
    public void Attempts_exhausted_lock_an_assignment_in_progress()
    {
        var assignment = Started();

        assignment.MarkLocked(Later(2));

        assignment.TestAccess.ShouldBe(TestAccess.LockedAfterFailedAttempts);
        assignment.TestAccessChangedAt.ShouldBe(Later(2));
        assignment.Status.ShouldBe(AssignmentStatus.InProgress);
        assignment.StateOn(Day(2)).ShouldBe(AssignmentState.Locked);
    }

    [Fact]
    public void Relearned_content_unlocks_a_lock_that_happened_before_it()
    {
        var assignment = Started();
        assignment.MarkLocked(Later(2));

        assignment.Unlock(Later(3));

        assignment.TestAccess.ShouldBe(TestAccess.Available);
        assignment.TestAccessChangedAt.ShouldBe(Later(3));
        assignment.Status.ShouldBe(AssignmentStatus.InProgress);
        assignment.StateOn(Day(3)).ShouldBe(AssignmentState.InProgress);
    }

    [Theory]
    [InlineData("CC2,AE3")]
    [InlineData("AE3,CC2")]
    public void Content_completed_before_the_lock_does_not_unlock_it_whichever_arrives_first(string order)
    {
        var assignment = Started();

        Apply(assignment, order);

        assignment.TestAccess.ShouldBe(TestAccess.LockedAfterFailedAttempts);
        assignment.TestAccessChangedAt.ShouldBe(Later(3));
    }

    [Theory]
    [InlineData("AE2,CC3")]
    [InlineData("CC3,AE2")]
    public void Content_completed_after_the_lock_unlocks_it_whichever_arrives_first(string order)
    {
        var assignment = Started();

        Apply(assignment, order);

        assignment.TestAccess.ShouldBe(TestAccess.Available);
        assignment.TestAccessChangedAt.ShouldBe(Later(3));
    }

    [Theory]
    [MemberData(nameof(EveryOrderOfALockBetweenTwoCompletions))]
    public void Every_order_of_lock_and_both_completions_ends_unlocked(string order)
    {
        var assignment = Started();

        Apply(assignment, order);

        assignment.TestAccess.ShouldBe(TestAccess.Available);
        assignment.TestAccessChangedAt.ShouldBe(Later(4));
    }

    [Theory]
    [MemberData(nameof(EveryOrderOfTwoCycles))]
    public void Every_order_of_two_cycles_ends_locked(string order)
    {
        var assignment = Started();

        Apply(assignment, order);

        assignment.TestAccess.ShouldBe(TestAccess.LockedAfterFailedAttempts);
        assignment.TestAccessChangedAt.ShouldBe(Later(5));
        assignment.StateOn(Day(5)).ShouldBe(AssignmentState.Locked);
    }

    [Fact]
    public void Each_event_twice_changes_nothing()
    {
        var assignment = Started();

        assignment.MarkLocked(Later(2));
        assignment.MarkLocked(Later(2));
        assignment.TestAccess.ShouldBe(TestAccess.LockedAfterFailedAttempts);
        assignment.TestAccessChangedAt.ShouldBe(Later(2));

        assignment.Unlock(Later(3));
        assignment.Unlock(Later(3));
        assignment.TestAccess.ShouldBe(TestAccess.Available);
        assignment.TestAccessChangedAt.ShouldBe(Later(3));

        // A dead letter of the old lock, replayed.
        assignment.MarkLocked(Later(2));
        assignment.TestAccess.ShouldBe(TestAccess.Available);
        assignment.TestAccessChangedAt.ShouldBe(Later(3));
        assignment.Status.ShouldBe(AssignmentStatus.InProgress);
        assignment.StartedAt.ShouldBe(Later(1));
    }

    [Theory]
    [InlineData("AE2,CC2")]
    [InlineData("CC2,AE2")]
    public void A_lock_and_an_unlock_at_the_same_instant_lock_in_either_order(string order)
    {
        var assignment = Started();

        Apply(assignment, order);

        assignment.TestAccess.ShouldBe(TestAccess.LockedAfterFailedAttempts);
        assignment.TestAccessChangedAt.ShouldBe(Later(2));
    }

    [Fact]
    public void A_lock_before_the_start_is_kept_and_shows_once_the_start_arrives()
    {
        var assignment = Assign();

        assignment.MarkLocked(Later(2));

        assignment.Status.ShouldBe(AssignmentStatus.Open);
        assignment.TestAccess.ShouldBe(TestAccess.LockedAfterFailedAttempts);
        assignment.TestAccessChangedAt.ShouldBe(Later(2));
        assignment.StateOn(Day(2)).ShouldBe(AssignmentState.Open);

        assignment.MarkStarted(Later(1));

        assignment.StateOn(Day(2)).ShouldBe(AssignmentState.Locked);
    }

    /// <summary>
    /// Owner decision on #247, option (a): Learning announces a session once, so a learner who began before the course
    /// was assigned is learning all the same; that start puts the assignment in progress, at the time it happened.
    /// </summary>
    [Fact]
    public void A_start_before_the_assignment_puts_it_in_progress_when_it_happened()
    {
        var assignment = Assign();

        assignment.MarkStarted(Later(-1));

        assignment.Status.ShouldBe(AssignmentStatus.InProgress);
        assignment.StartedAt.ShouldBe(Later(-1));

        assignment.MarkStarted(Later(1));

        assignment.StartedAt.ShouldBe(Later(-1));
    }

    [Fact]
    public void Locks_and_unlocks_that_happened_before_the_assignment_change_nothing()
    {
        var assignment = Assign();

        assignment.MarkLocked(Later(-1));
        assignment.Unlock(Later(-1));

        assignment.Status.ShouldBe(AssignmentStatus.Open);
        assignment.TestAccess.ShouldBe(TestAccess.Available);
        assignment.TestAccessChangedAt.ShouldBeNull();

        assignment.MarkStarted(Later(1));
        assignment.MarkLocked(Later(-1));

        assignment.TestAccess.ShouldBe(TestAccess.Available);
        assignment.TestAccessChangedAt.ShouldBeNull();
        assignment.StartedAt.ShouldBe(Later(1));
    }

    [Theory]
    [InlineData(AssignmentStatus.Completed)]
    [InlineData(AssignmentStatus.Cancelled)]
    public void A_completed_or_cancelled_assignment_ignores_start_lock_and_unlock(AssignmentStatus status)
    {
        var assignment = Started();
        if (status == AssignmentStatus.Completed)
        {
            assignment.Complete(CertificateId.From(Guid.CreateVersion7()), Later(2)).IsSuccess.ShouldBeTrue();
        }
        else
        {
            assignment.Cancel(Manager, ChangeReason.From("Versehen"), Later(2)).IsSuccess.ShouldBeTrue();
        }

        assignment.MarkStarted(Monday);
        assignment.MarkLocked(Later(3));

        assignment.Status.ShouldBe(status);
        assignment.StartedAt.ShouldBe(Later(1));
        assignment.TestAccess.ShouldBe(TestAccess.Available);
        assignment.TestAccessChangedAt.ShouldBeNull();

        assignment.Unlock(Later(4));

        assignment.Status.ShouldBe(status);
        assignment.TestAccess.ShouldBe(TestAccess.Available);
        assignment.TestAccessChangedAt.ShouldBeNull();
    }

    private static Assignment Assign() =>
        Assignment.Assign(AssignmentId.New(), Anna, Pharmacy, Hygiene, DueDate.On(Day(30)), null, Manager, Monday).Value;

    /// <summary>An assignment the learner started on day 1.</summary>
    private static Assignment Started()
    {
        var assignment = Assign();
        assignment.MarkStarted(Later(1));
        return assignment;
    }

    /// <summary>Applies the events in the order given, such as <c>"AE3,CC2"</c>.</summary>
    private static void Apply(Assignment assignment, string order)
    {
        foreach (var step in order.Split(','))
        {
            var at = Later(int.Parse(step[2..], System.Globalization.CultureInfo.InvariantCulture));
            switch (step[..2])
            {
                case "AE":
                    assignment.MarkLocked(at);
                    break;
                case "CC":
                    assignment.Unlock(at);
                    break;
                default:
                    throw new InvalidOperationException($"No event is written {step}.");
            }
        }
    }

    private static TheoryData<string> Orders(params string[] events)
    {
        var orders = new TheoryData<string>();
        foreach (var order in Permutations(events))
        {
            orders.Add(string.Join(',', order));
        }

        return orders;
    }

    private static IEnumerable<IReadOnlyList<string>> Permutations(IReadOnlyList<string> events)
    {
        if (events.Count <= 1)
        {
            yield return events;
            yield break;
        }

        for (var index = 0; index < events.Count; index++)
        {
            var first = events[index];
            List<string> rest = [.. events.Where((_, other) => other != index)];
            foreach (var tail in Permutations(rest))
            {
                yield return [first, .. tail];
            }
        }
    }

    private static CalendarDate Day(int daysFromMonday) => CalendarDate.From(new DateOnly(2027, 3, 1).AddDays(daysFromMonday));

    private static Timestamp Later(int days) => Timestamp.From(Monday.Value.AddDays(days));
}
