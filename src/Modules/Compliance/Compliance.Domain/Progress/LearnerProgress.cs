using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Progress;

/// <summary>
/// A learner's session starts and certificates of one course, as Compliance holds them, and which session is running
/// (docs/adr/0024): the latest start after the latest certificate of the course, of any version, or the latest start at
/// all without a certificate. A start at the certificate's instant is finished; of two starts at one instant the greater
/// session id runs. Only the instants decide, never the order the copies arrived in.
/// </summary>
public sealed class LearnerProgress
{
    private readonly IReadOnlyList<LearningSessionStart> starts;
    private readonly IReadOnlyList<IssuedCertificate> certificates;

    private LearnerProgress(PersonId learner, CourseId course, IReadOnlyList<LearningSessionStart> starts, IReadOnlyList<IssuedCertificate> certificates)
    {
        Learner = learner;
        Course = course;
        this.starts = starts;
        this.certificates = certificates;
    }

    public PersonId Learner { get; }

    public CourseId Course { get; }

    /// <summary>The learner's progress in the course; every start and certificate given must be the learner's, of the course.</summary>
    public static LearnerProgress Of(
        PersonId learner,
        CourseId course,
        IReadOnlyCollection<LearningSessionStart> starts,
        IReadOnlyCollection<IssuedCertificate> certificates)
    {
        Ensure.That(learner).IsNotNull();
        Ensure.That(course).IsNotNull();
        Ensure.That(starts).IsNotNull();
        Ensure.That(certificates).IsNotNull();
        foreach (var start in starts)
        {
            Ensure.That(start, nameof(starts)).IsNotNull();
            Ensure.That(start.Id.ToString(), nameof(starts)).Satisfies(_ => start.Learner == learner && start.Course == course, $"session {start.Id} belongs to another learner or course than {learner} in {course}.");
        }

        foreach (var certificate in certificates)
        {
            Ensure.That(certificate, nameof(certificates)).IsNotNull();
            Ensure.That(certificate.Id.ToString(), nameof(certificates)).Satisfies(_ => certificate.Learner == learner && certificate.Course == course, $"certificate {certificate.Id} belongs to another learner or course than {learner} in {course}.");
        }

        return new LearnerProgress(learner, course, [.. starts], [.. certificates]);
    }

    /// <summary>The session the learner has not finished yet; none when every start was followed by a certificate.</summary>
    public Option<LearningSessionStart> RunningSession()
    {
        var finishedUntil = certificates.Select(certificate => certificate.IssuedAt).Max();
        var running = starts
            .Where(start => finishedUntil is null || start.StartedAt > finishedUntil)
            .OrderByDescending(start => start.StartedAt)
            .ThenByDescending(start => start.Id.Value)
            .FirstOrDefault();
        if (running is null)
        {
            return Option<LearningSessionStart>.None;
        }

        return Option<LearningSessionStart>.Some(running);
    }
}
