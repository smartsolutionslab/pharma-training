using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Progress;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;

/// <summary>
/// Compliance's copies of Learning's session starts and Certification's certificates; implemented in its Infrastructure
/// project. What is added is saved when the module's unit of work commits; the consumers never change what they stored
/// (docs/adr/0024).
/// </summary>
public interface ILearnerProgressStore
{
    Task<bool> HasSessionStartAsync(LearningSessionId session, CancellationToken cancellationToken);

    void Add(LearningSessionStart start);

    Task<bool> HasCertificateAsync(CertificateId certificate, CancellationToken cancellationToken);

    void Add(IssuedCertificate certificate);

    /// <summary>One per asked course (distinct), also when nothing is stored for it; read without tracking.</summary>
    Task<IReadOnlyList<LearnerProgress>> OfLearnerAsync(PersonId learner, IReadOnlyCollection<CourseId> courses, CancellationToken cancellationToken);
}
