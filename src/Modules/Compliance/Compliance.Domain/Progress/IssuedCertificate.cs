using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Domain;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Assignments;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Members;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Progress;

/// <summary>
/// Compliance's copy of a certificate Certification issued: whose, of which course and version, and when. Certification
/// owns it; Compliance stores it once, as it first arrived, and never changes it (docs/adr/0028).
/// </summary>
public sealed class IssuedCertificate : Entity<CertificateId>
{
    private IssuedCertificate(CertificateId id, PersonId learner, CourseId course, CourseVersionId version, Timestamp issuedAt)
        : base(id)
    {
        Learner = learner;
        Course = course;
        Version = version;
        IssuedAt = issuedAt;
    }

    public PersonId Learner { get; }

    public CourseId Course { get; }

    public CourseVersionId Version { get; }

    public Timestamp IssuedAt { get; }

    public static IssuedCertificate Of(CertificateId id, PersonId learner, CourseId course, CourseVersionId version, Timestamp issuedAt)
    {
        Ensure.That(id).IsNotNull();
        Ensure.That(learner).IsNotNull();
        Ensure.That(course).IsNotNull();
        Ensure.That(version).IsNotNull();
        Ensure.That(issuedAt).IsNotNull();
        return new IssuedCertificate(id, learner, course, version, issuedAt);
    }
}
