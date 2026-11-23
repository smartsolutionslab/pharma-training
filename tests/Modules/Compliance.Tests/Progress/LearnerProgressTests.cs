using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Progress;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Progress;

/// <summary>
/// Which of a learner's sessions of a course is running (ADR 0024): the latest start after the latest certificate of the
/// course, of any version, or the latest start at all without a certificate; a start at the certificate's instant is
/// finished, and of two starts at one instant the greater session id runs. Only the instants decide, never the order the
/// copies arrived in.
/// </summary>
public class LearnerProgressTests
{
    private static readonly DateTimeOffset Monday = new(2027, 3, 1, 9, 0, 0, TimeSpan.FromHours(1));
    private static readonly PersonId Learner = PersonId.From(Guid.CreateVersion7());
    private static readonly CourseId Course = CourseId.From(Guid.CreateVersion7());

    [Fact]
    public void Without_starts_no_session_runs()
    {
        LearnerProgress.Of(Learner, Course, [], []).RunningSession().HasValue.ShouldBeFalse();
        LearnerProgress.Of(Learner, Course, [], [Certificate(At(1))]).RunningSession().HasValue.ShouldBeFalse();
    }

    [Fact]
    public void The_progress_is_the_learners_of_the_course()
    {
        var progress = LearnerProgress.Of(Learner, Course, [], []);

        progress.Learner.ShouldBe(Learner);
        progress.Course.ShouldBe(Course);
    }

    [Fact]
    public void Without_a_certificate_the_latest_start_runs()
    {
        var first = Start(At(1));
        var latest = Start(At(3));
        var between = Start(At(2));

        var running = LearnerProgress.Of(Learner, Course, [first, latest, between], []).RunningSession();

        running.HasValue.ShouldBeTrue();
        running.Value.ShouldBeSameAs(latest);
    }

    [Fact]
    public void A_start_before_the_latest_certificate_of_the_course_is_finished_whatever_version_the_certificate_is_for()
    {
        var onFirstVersion = CourseVersionId.From(Guid.CreateVersion7());
        var onSecondVersion = CourseVersionId.From(Guid.CreateVersion7());
        var start = Start(At(2), version: onFirstVersion);

        var progress = LearnerProgress.Of(Learner, Course, [start], [Certificate(At(1)), Certificate(At(3), onSecondVersion)]);

        progress.RunningSession().HasValue.ShouldBeFalse();
    }

    [Fact]
    public void A_start_after_the_latest_certificate_runs()
    {
        var finished = Start(At(1));
        var afterTheFirstCertificate = Start(At(3));
        var afterTheLatestCertificate = Start(At(5));

        var running = LearnerProgress.Of(
            Learner,
            Course,
            [finished, afterTheFirstCertificate, afterTheLatestCertificate],
            [Certificate(At(2)), Certificate(At(4))]).RunningSession();

        running.HasValue.ShouldBeTrue();
        running.Value.ShouldBeSameAs(afterTheLatestCertificate);
    }

    [Fact]
    public void An_older_start_does_not_run_once_a_certificate_followed_it_even_if_no_start_came_after()
    {
        var progress = LearnerProgress.Of(Learner, Course, [Start(At(1)), Start(At(3))], [Certificate(At(4))]);

        progress.RunningSession().HasValue.ShouldBeFalse();
    }

    [Fact]
    public void A_start_at_the_certificates_instant_is_finished()
    {
        var progress = LearnerProgress.Of(Learner, Course, [Start(At(2))], [Certificate(At(2))]);

        progress.RunningSession().HasValue.ShouldBeFalse();
    }

    [Fact]
    public void Of_two_starts_at_one_instant_the_greater_session_id_runs()
    {
        var one = Start(At(2));
        var other = Start(At(2));
        var greater = Comparer<Guid>.Default.Compare(one.Id.Value, other.Id.Value) > 0 ? one : other;

        var running = LearnerProgress.Of(Learner, Course, [one, other], []).RunningSession();

        running.HasValue.ShouldBeTrue();
        running.Value.ShouldBeSameAs(greater);
    }

    [Fact]
    public void The_order_the_copies_arrived_in_does_not_matter()
    {
        List<LearningSessionStart> starts = [Start(At(1)), Start(At(4)), Start(At(4)), Start(At(2))];
        List<IssuedCertificate> certificates = [Certificate(At(3)), Certificate(At(0))];
        var greaterAtFour = starts.Where(start => start.StartedAt == Timestamp.From(At(4))).MaxBy(start => start.Id.Value).ShouldNotBeNull();

        var inOrder = LearnerProgress.Of(Learner, Course, starts, certificates).RunningSession();
        var reversed = LearnerProgress.Of(Learner, Course, [.. Enumerable.Reverse(starts)], [.. Enumerable.Reverse(certificates)]).RunningSession();

        inOrder.Value.ShouldBeSameAs(greaterAtFour);
        reversed.Value.ShouldBeSameAs(greaterAtFour);
    }

    [Theory]
    [InlineData("start of another learner")]
    [InlineData("start of another course")]
    [InlineData("certificate of another learner")]
    [InlineData("certificate of another course")]
    public void Of_refuses_a_start_or_certificate_of_another_learner_or_course(string stranger)
    {
        var otherLearner = PersonId.From(Guid.CreateVersion7());
        var otherCourse = CourseId.From(Guid.CreateVersion7());
        List<LearningSessionStart> starts = [Start(At(1))];
        List<IssuedCertificate> certificates = [Certificate(At(2))];
        switch (stranger)
        {
            case "start of another learner":
                starts.Add(Start(At(3), learner: otherLearner));
                break;
            case "start of another course":
                starts.Add(Start(At(3), course: otherCourse));
                break;
            case "certificate of another learner":
                certificates.Add(Certificate(At(3), learner: otherLearner));
                break;
            default:
                certificates.Add(Certificate(At(3), course: otherCourse));
                break;
        }

        Should.Throw<GuardException>(() => LearnerProgress.Of(Learner, Course, starts, certificates));
    }

    private static DateTimeOffset At(int hoursAfterMonday) => Monday.AddHours(hoursAfterMonday);

    private static LearningSessionStart Start(DateTimeOffset at, CourseVersionId? version = null, PersonId? learner = null, CourseId? course = null) =>
        LearningSessionStart.Of(
            LearningSessionId.From(Guid.CreateVersion7()),
            learner ?? Learner,
            course ?? Course,
            version ?? CourseVersionId.From(Guid.CreateVersion7()),
            Timestamp.From(at));

    private static IssuedCertificate Certificate(DateTimeOffset at, CourseVersionId? version = null, PersonId? learner = null, CourseId? course = null) =>
        IssuedCertificate.Of(
            CertificateId.From(Guid.CreateVersion7()),
            learner ?? Learner,
            course ?? Course,
            version ?? CourseVersionId.From(Guid.CreateVersion7()),
            Timestamp.From(at));
}
