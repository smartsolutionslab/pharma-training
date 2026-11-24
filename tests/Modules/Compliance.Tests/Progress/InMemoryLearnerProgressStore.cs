using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Progress;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Tests.Progress;

/// <summary>
/// Compliance's copies of Learning's session starts and Certification's certificates in memory. What is added is stored
/// at once: this store is not a unit of work, so the consumers' commits are asserted through the
/// <see cref="Assignments.InMemoryAssignmentStore"/> passed to them as the unit of work. A learner's progress is read as
/// the store in Infrastructure reads it, through <see cref="LearnerProgress.Of"/>, one per asked course.
/// </summary>
internal sealed class InMemoryLearnerProgressStore : ILearnerProgressStore
{
    public List<LearningSessionStart> Starts { get; } = [];

    public List<IssuedCertificate> Certificates { get; } = [];

    public Task<bool> HasSessionStartAsync(LearningSessionId session, CancellationToken cancellationToken) =>
        Task.FromResult(Starts.Any(start => start.Id == session));

    public void Add(LearningSessionStart start) => Starts.Add(start);

    public Task<bool> HasCertificateAsync(CertificateId certificate, CancellationToken cancellationToken) =>
        Task.FromResult(Certificates.Any(issued => issued.Id == certificate));

    public void Add(IssuedCertificate certificate) => Certificates.Add(certificate);

    public Task<IReadOnlyList<LearnerProgress>> OfLearnerAsync(PersonId learner, IReadOnlyCollection<CourseId> courses, CancellationToken cancellationToken)
    {
        IReadOnlyList<LearnerProgress> progress =
        [
            .. courses.Distinct().Select(course => LearnerProgress.Of(
                learner,
                course,
                [.. Starts.Where(start => start.Learner == learner && start.Course == course)],
                [.. Certificates.Where(certificate => certificate.Learner == learner && certificate.Course == course)])),
        ];
        return Task.FromResult(progress);
    }
}
