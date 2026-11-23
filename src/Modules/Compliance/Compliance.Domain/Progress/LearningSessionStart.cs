using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Progress;

/// <summary>
/// Compliance's copy of a session Learning started: whose, of which course and version, and when. Learning owns it;
/// Compliance stores it once, as it first arrived, and never changes it (docs/adr/0024).
/// </summary>
public sealed class LearningSessionStart : Entity<LearningSessionId>
{
    private LearningSessionStart(LearningSessionId id, PersonId learner, CourseId course, CourseVersionId version, Timestamp startedAt)
        : base(id)
    {
        Learner = learner;
        Course = course;
        Version = version;
        StartedAt = startedAt;
    }

    public PersonId Learner { get; }

    public CourseId Course { get; }

    public CourseVersionId Version { get; }

    public Timestamp StartedAt { get; }

    public static LearningSessionStart Of(LearningSessionId id, PersonId learner, CourseId course, CourseVersionId version, Timestamp startedAt)
    {
        Ensure.That(id).IsNotNull();
        Ensure.That(learner).IsNotNull();
        Ensure.That(course).IsNotNull();
        Ensure.That(version).IsNotNull();
        Ensure.That(startedAt).IsNotNull();
        return new LearningSessionStart(id, learner, course, version, startedAt);
    }
}
