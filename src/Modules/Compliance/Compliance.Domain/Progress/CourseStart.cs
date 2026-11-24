using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Progress;

/// <summary>
/// The course version a learner opens for an assignment (decision "Matrix und Startversion", docs/adr/0028): a running
/// session's own version to resume, whatever became of that version since; else the course's current version to begin;
/// else none, when the latest version is retired.
/// </summary>
public sealed class CourseStart
{
    private CourseStart(CourseVersionId version, CourseStartKind kind)
    {
        Version = version;
        Kind = kind;
    }

    public CourseVersionId Version { get; }

    public CourseStartKind Kind { get; }

    public static Option<CourseStart> For(Option<LearningSessionStart> running, PublishedCourse course)
    {
        Ensure.That(course).IsNotNull();

        if (running.HasValue)
        {
            var session = running.Value;
            Ensure.That(session.Course.ToString(), nameof(running)).Satisfies(_ => session.Course == course.Course, $"session {session.Id} belongs to another course than {course.Course}.");
            return Option<CourseStart>.Some(new CourseStart(session.Version, CourseStartKind.Resume));
        }

        var current = course.CurrentVersion();
        if (!current.HasValue)
        {
            return Option<CourseStart>.None;
        }

        return Option<CourseStart>.Some(new CourseStart(current.Value.Id, CourseStartKind.Begin));
    }
}
