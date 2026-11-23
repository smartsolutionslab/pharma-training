using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Progress;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Progress;

/// <summary>
/// The course version a learner opens for an assignment (decision "Matrix und Startversion", ADR 0024): a running
/// session's own version to resume, whatever became of it since; else the course's current version to begin; else none,
/// when the latest version is retired.
/// </summary>
public class CourseStartTests
{
    private static readonly DateTimeOffset Monday = new(2027, 3, 1, 9, 0, 0, TimeSpan.FromHours(1));
    private static readonly PersonId Learner = PersonId.From(Guid.CreateVersion7());

    private readonly CourseId course = CourseId.From(Guid.CreateVersion7());
    private readonly CourseVersionId first = CourseVersionId.From(Guid.CreateVersion7());
    private readonly CourseVersionId second = CourseVersionId.From(Guid.CreateVersion7());

    [Fact]
    public void A_running_session_resumes_its_own_version_although_a_newer_one_is_current()
    {
        var start = CourseStart.For(Running(first), Published([first, second]));

        start.HasValue.ShouldBeTrue();
        start.Value.Version.ShouldBe(first);
        start.Value.Kind.ShouldBe(CourseStartKind.Resume);
    }

    [Fact]
    public void A_running_session_resumes_its_version_also_when_that_version_is_retired()
    {
        var start = CourseStart.For(Running(first), Published([first, second], retired: [first]));

        start.Value.Version.ShouldBe(first);
        start.Value.Kind.ShouldBe(CourseStartKind.Resume);
    }

    [Fact]
    public void A_running_session_resumes_its_version_also_when_the_latest_version_is_retired()
    {
        var start = CourseStart.For(Running(second), Published([first, second], retired: [second]));

        start.Value.Version.ShouldBe(second);
        start.Value.Kind.ShouldBe(CourseStartKind.Resume);
    }

    [Fact]
    public void A_running_session_resumes_its_version_also_when_Compliance_never_received_that_version()
    {
        var unknown = CourseVersionId.From(Guid.CreateVersion7());

        var start = CourseStart.For(Running(unknown), Published([first]));

        start.Value.Version.ShouldBe(unknown);
        start.Value.Kind.ShouldBe(CourseStartKind.Resume);
    }

    [Fact]
    public void Without_a_session_the_current_version_is_begun()
    {
        var start = CourseStart.For(Option<LearningSessionStart>.None, Published([first, second]));

        start.HasValue.ShouldBeTrue();
        start.Value.Version.ShouldBe(second);
        start.Value.Kind.ShouldBe(CourseStartKind.Begin);
    }

    [Fact]
    public void Without_a_session_and_with_the_latest_version_retired_nothing_is_started()
    {
        var start = CourseStart.For(Option<LearningSessionStart>.None, Published([first, second], retired: [second]));

        start.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void A_running_start_of_another_course_is_refused()
    {
        var elsewhere = LearningSessionStart.Of(
            LearningSessionId.From(Guid.CreateVersion7()),
            Learner,
            CourseId.From(Guid.CreateVersion7()),
            first,
            Timestamp.From(Monday));

        Should.Throw<GuardException>(() => CourseStart.For(Option<LearningSessionStart>.Some(elsewhere), Published([first])));
    }

    private Option<LearningSessionStart> Running(CourseVersionId version) =>
        Option<LearningSessionStart>.Some(LearningSessionStart.Of(LearningSessionId.From(Guid.CreateVersion7()), Learner, course, version, Timestamp.From(Monday)));

    /// <summary>The course with the versions numbered in the order given, the retired ones among them retired.</summary>
    private PublishedCourse Published(IReadOnlyList<CourseVersionId> versions, IReadOnlyList<CourseVersionId>? retired = null)
    {
        List<CourseVersion> published =
        [
            .. versions.Select((version, index) => CourseVersion.Published(version, course, VersionNumber.From(index + 1), CourseTitle.From($"Hygiene {index + 1}"), ValidityMonths.From(24))),
        ];
        List<CourseVersionRetirement> retirements = [.. (retired ?? []).Select(version => CourseVersionRetirement.Of(version, course))];
        return PublishedCourse.Of(course, published, retirements).Value;
    }
}
