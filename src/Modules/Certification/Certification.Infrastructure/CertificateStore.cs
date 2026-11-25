using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Retention;
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

    public async Task<Option<Certificate>> LoadAsync(CertificateId id, CancellationToken cancellationToken) =>
        await context.Set<Certificate>().SingleOrDefaultAsync(certificate => certificate.Id == id, cancellationToken) is { } certificate
            ? Option<Certificate>.Some(certificate)
            : Option<Certificate>.None;

    public async Task<Option<Certificate>> FindAsync(CertificateId id, CancellationToken cancellationToken) =>
        await context.Set<Certificate>().AsNoTracking().SingleOrDefaultAsync(certificate => certificate.Id == id, cancellationToken) is { } certificate
            ? Option<Certificate>.Some(certificate)
            : Option<Certificate>.None;

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

    public async Task<IReadOnlyList<Certificate>> IssuedInPharmacyAsync(PharmacyId pharmacy, Timestamp issuedFrom, Timestamp issuedBefore, Timestamp keptAt, RetentionPeriod keptFor, int limit, CancellationToken cancellationToken)
    {
        Ensure.That(pharmacy).IsNotNull();
        Ensure.That(keptAt).IsNotNull();
        Ensure.That(keptFor).IsNotNull();
        Ensure.That(limit).AtLeast(1);

        // Retention (#53, docs/adr/0034, 0039) is Certificate.IsKeptByPharmaciesAt, IssuedAt.AddYears(n) > now for the
        // pharmacy's period of n years, evaluated here so that the limit counts only the certificates kept. It is the
        // anniversary on the UTC calendar: timestamp arithmetic
        // in UTC whatever the session's time zone, and PostgreSQL, like AddYears, takes 29 February to 28 February. It is not
        // written as issued_at > now - n years: around 29 February no single lower bound matches AddYears (a certificate
        // issued on 28 February at 13:00 outlasts one issued on 29 February at 01:00), and certificates would reappear.
        // The years are a parameter of the statement, never interpolated into its text.
        var keptYears = keptFor.Value;
        var now = keptAt.Value;

        // EXISTS rather than a join, so a holder with two overlapping memberships in the pharmacy yields each certificate once.
        return await context.Set<Certificate>()
            .FromSql($"SELECT * FROM certification.certificates WHERE (issued_at AT TIME ZONE 'UTC') + make_interval(years => {keptYears}) > ({now} AT TIME ZONE 'UTC')")
            .AsNoTracking()
            .Where(certificate => certificate.IssuedAt >= issuedFrom && certificate.IssuedAt < issuedBefore)
            .Where(certificate => context.Set<Membership>().Any(membership =>
                membership.Pharmacy == pharmacy
                && membership.Person == certificate.Holder
                && membership.StartedAt <= certificate.IssuedAt
                && !context.Set<MembershipEnding>().Any(ending => ending.Id == membership.Id && ending.EndedAt <= certificate.IssuedAt)))
            .OrderBy(certificate => certificate.IssuedAt)
            .ThenBy(certificate => certificate.Id)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<Certificate> Items, int TotalCount)> ListForHolderAsync(PersonId holder, Page page, PageSize pageSize, CancellationToken cancellationToken)
    {
        Ensure.That(holder).IsNotNull();
        Ensure.That(page).IsNotNull();
        Ensure.That(pageSize).IsNotNull();

        var holders = context.Set<Certificate>().AsNoTracking().Where(certificate => certificate.Holder == holder);
        var total = await holders.CountAsync(cancellationToken);
        var items = await holders
            .OrderByDescending(certificate => certificate.IssuedAt)
            .ThenByDescending(certificate => certificate.Id)
            .Skip(page.Skip(pageSize))
            .Take(pageSize.Value)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public void Add(Certificate certificate) => context.Add(certificate);
}
