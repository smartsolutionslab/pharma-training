namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;

/// <summary>
/// A course as Compliance knows it from the versions and retirements that have arrived, in whatever order. Its latest
/// version is the one with the highest number, and it names the course; that version is current unless it is retired,
/// and then the course has no current version: an older one was superseded in Editorial already. A retirement that
/// arrived before its version names no version received and changes nothing until that version arrives, which it then
/// retires, as Catalog lists courses.
/// </summary>
public sealed class PublishedCourse
{
    private PublishedCourse(CourseId course, CourseVersion latest, PublishedCourseState state)
    {
        Course = course;
        Latest = latest;
        State = state;
    }

    public CourseId Course { get; }

    /// <summary>The version with the highest number, retired or not.</summary>
    public CourseVersion Latest { get; }

    public CourseTitle Title => Latest.Title;

    /// <summary>Whether the latest version is available or retired, which leaves the course without a current version.</summary>
    public PublishedCourseState State { get; }

    /// <summary>The course from its versions and retirements; none while no version of it has arrived.</summary>
    public static Option<PublishedCourse> Of(CourseId course, IReadOnlyCollection<CourseVersion> versions, IReadOnlyCollection<CourseVersionRetirement> retirements)
    {
        Ensure.That(course).IsNotNull();
        Ensure.That(versions).IsNotNull();
        Ensure.That(retirements).IsNotNull();
        foreach (var version in versions)
        {
            Ensure.That(version, nameof(versions)).IsNotNull();
            Ensure.That(version.Course.ToString(), nameof(versions)).Satisfies(_ => version.Course == course, $"version {version.Id} belongs to another course than {course}.");
        }

        foreach (var retirement in retirements)
        {
            Ensure.That(retirement, nameof(retirements)).IsNotNull();
            Ensure.That(retirement.Course.ToString(), nameof(retirements)).Satisfies(_ => retirement.Course == course, $"retirement of {retirement.Id} belongs to another course than {course}.");
        }

        if (versions.Count == 0)
        {
            return Option<PublishedCourse>.None;
        }

        var latest = versions.OrderByDescending(version => version.Number).First();
        var state = retirements.Any(retirement => retirement.Id == latest.Id) ? PublishedCourseState.Retired : PublishedCourseState.Available;
        return Option<PublishedCourse>.Some(new PublishedCourse(course, latest, state));
    }

    /// <summary>The version new sessions start on: the latest, unless it is retired.</summary>
    public Option<CourseVersion> CurrentVersion()
    {
        if (State == PublishedCourseState.Retired)
        {
            return Option<CourseVersion>.None;
        }

        return Option<CourseVersion>.Some(Latest);
    }
}
