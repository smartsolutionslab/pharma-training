using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;

/// <summary>
/// The certificates Certification issued; implemented in its Infrastructure project. What is added is saved when the
/// module's unit of work commits.
/// </summary>
public interface ICertificateStore
{
    /// <summary>Whether a certificate was issued for the attempt, the natural key of a passed test.</summary>
    Task<bool> ExistsForAttemptAsync(AttemptId attempt, CancellationToken cancellationToken);

    void Add(Certificate certificate);
}
