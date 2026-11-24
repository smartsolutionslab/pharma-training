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

    /// <summary>The certificate, tracked, to change it and save it with the module's unit of work; none when no certificate has the id.</summary>
    Task<Option<Certificate>> LoadAsync(CertificateId id, CancellationToken cancellationToken);

    /// <summary>The certificate as stored, read without tracking, to answer a query; none when no certificate has the id.</summary>
    Task<Option<Certificate>> FindAsync(CertificateId id, CancellationToken cancellationToken);

    /// <summary>The certificate the code names, read without tracking, if any.</summary>
    Task<Option<Certificate>> FindByVerificationCodeAsync(VerificationCode code, CancellationToken cancellationToken);

    /// <summary>
    /// The holders' certificates issued from <paramref name="issuedFrom"/> on and before <paramref name="issuedBefore"/>,
    /// read without tracking, oldest first, then by id.
    /// </summary>
    Task<IReadOnlyList<Certificate>> IssuedToAsync(IReadOnlyCollection<PersonId> holders, Timestamp issuedFrom, Timestamp issuedBefore, CancellationToken cancellationToken);

    /// <summary>
    /// A page of the holder's certificates, read without tracking, newest first (issued later, then the larger id), and
    /// how many the holder has in all.
    /// </summary>
    Task<(IReadOnlyList<Certificate> Items, int TotalCount)> ListForHolderAsync(PersonId holder, Page page, PageSize pageSize, CancellationToken cancellationToken);

    void Add(Certificate certificate);
}
