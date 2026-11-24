using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure;

/// <summary>
/// The certificates. The module's unit of work saves what is added; the unique index on the attempt refuses a second
/// certificate for one attempt, so two concurrent deliveries of one passed test race only on it.
/// </summary>
public sealed class CertificateStore(CertificationDbContext context) : ICertificateStore
{
    public Task<bool> ExistsForAttemptAsync(AttemptId attempt, CancellationToken cancellationToken) =>
        context.Set<Certificate>().AnyAsync(certificate => certificate.Attempt == attempt, cancellationToken);

    public async Task<Option<Certificate>> FindByVerificationCodeAsync(VerificationCode code, CancellationToken cancellationToken)
    {
        var certificate = await context.Set<Certificate>()
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.VerificationCode == code, cancellationToken);
        if (certificate is null)
        {
            return Option<Certificate>.None;
        }

        return Option<Certificate>.Some(certificate);
    }

    public void Add(Certificate certificate) => context.Add(certificate);
}
