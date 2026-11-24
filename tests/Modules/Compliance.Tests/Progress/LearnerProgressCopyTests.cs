using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Progress;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Assignments;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Progress;

/// <summary>
/// Compliance keeps a copy of every session start and every certificate it receives (ADR 0028), whether or not the person
/// has an active assignment of the course, once per session or certificate id, so the dashboard can tell which session
/// is running. A copy is saved with the consumer's one commit, which the assignment store counts as the unit of work.
/// </summary>
public class LearnerProgressCopyTests
{
    private static readonly DateTimeOffset Monday = new(2027, 3, 1, 9, 0, 0, TimeSpan.FromHours(1));
    private static readonly PersonId Manager = PersonId.From(Guid.CreateVersion7());

    private readonly InMemoryAssignmentStore store = new();
    private readonly InMemoryLearnerProgressStore progress = new();
    private readonly Guid anna = Guid.CreateVersion7();
    private readonly Guid hygiene = Guid.CreateVersion7();

    [Fact]
    public async Task A_start_is_kept_with_its_learner_course_version_and_instant_and_committed_without_an_active_assignment()
    {
        var started = LearningEvents.Started(anna, hygiene, At(2));

        await StartedConsumer().HandleAsync(started, TestContext.Current.CancellationToken);

        var kept = progress.Starts.ShouldHaveSingleItem();
        kept.Id.ShouldBe(LearningSessionId.From(started.SessionId));
        kept.Learner.ShouldBe(PersonId.From(anna));
        kept.Course.ShouldBe(CourseId.From(hygiene));
        kept.Version.ShouldBe(CourseVersionId.From(started.CourseVersionId));
        kept.StartedAt.ShouldBe(Timestamp.From(At(2)));
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_start_is_kept_and_puts_the_active_assignment_in_progress_in_one_commit()
    {
        var assignment = Assign(anna, hygiene);
        var started = LearningEvents.Started(anna, hygiene, At(2));

        await StartedConsumer().HandleAsync(started, TestContext.Current.CancellationToken);

        progress.Starts.ShouldHaveSingleItem().Id.ShouldBe(LearningSessionId.From(started.SessionId));
        assignment.Status.ShouldBe(AssignmentStatus.InProgress);
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task The_same_start_delivered_twice_is_kept_once_and_the_second_delivery_commits_nothing_when_nothing_is_active()
    {
        var started = LearningEvents.Started(anna, hygiene, At(2));

        await StartedConsumer().HandleAsync(started, TestContext.Current.CancellationToken);
        await StartedConsumer().HandleAsync(started, TestContext.Current.CancellationToken);

        progress.Starts.ShouldHaveSingleItem().Id.ShouldBe(LearningSessionId.From(started.SessionId));
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_certificate_is_kept_with_its_version_and_issue_time_and_committed_without_an_active_assignment()
    {
        var issued = CertificateEvents.Issued(anna, hygiene, At(5));

        await CertificateConsumer().HandleAsync(issued, TestContext.Current.CancellationToken);

        var kept = progress.Certificates.ShouldHaveSingleItem();
        kept.Id.ShouldBe(CertificateId.From(issued.CertificateId));
        kept.Learner.ShouldBe(PersonId.From(anna));
        kept.Course.ShouldBe(CourseId.From(hygiene));
        kept.Version.ShouldBe(CourseVersionId.From(issued.CourseVersionId));
        kept.IssuedAt.ShouldBe(Timestamp.From(At(5)));
        store.Published.ShouldBeEmpty();
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_certificate_older_than_the_only_assignment_is_kept_and_leaves_the_assignment_open()
    {
        var assignment = Assign(anna, hygiene);
        var issued = CertificateEvents.Issued(anna, hygiene, Monday.AddMinutes(-1));

        await CertificateConsumer().HandleAsync(issued, TestContext.Current.CancellationToken);

        progress.Certificates.ShouldHaveSingleItem().IssuedAt.ShouldBe(Timestamp.From(Monday.AddMinutes(-1)));
        assignment.Status.ShouldBe(AssignmentStatus.Open);
        assignment.Certificate.ShouldBeNull();
        store.Published.ShouldBeEmpty();
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task The_same_certificate_delivered_twice_is_kept_once()
    {
        var issued = CertificateEvents.Issued(anna, hygiene, At(5));

        await CertificateConsumer().HandleAsync(issued, TestContext.Current.CancellationToken);
        await CertificateConsumer().HandleAsync(issued, TestContext.Current.CancellationToken);

        progress.Certificates.ShouldHaveSingleItem().Id.ShouldBe(CertificateId.From(issued.CertificateId));
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task A_refused_start_keeps_nothing()
    {
        var started = LearningEvents.Started(anna, hygiene, At(2)) with { SessionId = Guid.Empty };

        await Should.ThrowAsync<GuardException>(() => StartedConsumer().HandleAsync(started, TestContext.Current.CancellationToken));

        progress.Starts.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task A_refused_certificate_keeps_nothing()
    {
        var issued = CertificateEvents.Issued(anna, hygiene, At(5)) with { CertificateId = Guid.Empty };

        await Should.ThrowAsync<GuardException>(() => CertificateConsumer().HandleAsync(issued, TestContext.Current.CancellationToken));

        progress.Certificates.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    private static DateTimeOffset At(int hoursAfterMonday) => Monday.AddHours(hoursAfterMonday);

    // Compliance's clock reads a week after Monday, later than every event these tests send (docs/adr/0023 clamps later ones).
    private LearningSessionStartedV1Consumer StartedConsumer() => new(store, progress, store, new FakeTimeProvider(Monday.AddDays(7)));

    private CertificateIssuedV1Consumer CertificateConsumer() => new(store, progress, store);

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
}
