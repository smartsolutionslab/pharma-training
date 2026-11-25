using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Retention;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Certificates;

/// <summary>
/// The certificates table in memory, and the module's unit of work over it: what is added is stored, and what is
/// published is sent, only once committed, as the outbox sends it in the same transaction. A certificate loaded is the
/// stored one itself, as EF Core tracks it, so what is changed on it is what a commit saves.
/// </summary>
internal sealed class InMemoryCertificateStore : ICertificateStore, ICertificationUnitOfWork
{
    private readonly List<Certificate> added = [];
    private readonly List<Certificate> removed = [];
    private readonly List<IntegrationEvent> queued = [];
    private readonly InMemoryMembershipStore? memberships;

    public InMemoryCertificateStore()
    {
    }

    /// <summary>A store whose export reads choose certificates by the memberships in <paramref name="memberships"/>.</summary>
    public InMemoryCertificateStore(InMemoryMembershipStore memberships)
    {
        this.memberships = memberships;
    }

    public List<Certificate> Stored { get; } = [];

    public List<IntegrationEvent> Published { get; } = [];

    public int Commits { get; private set; }

    /// <summary>Where the reads of a holder's certificates are written down, in order with the rest of a test's (#115); none by default.</summary>
    public List<string>? Journal { get; set; }

    /// <summary>How often a certificate was looked up by its verification code.</summary>
    public int VerificationLookups { get; private set; }

    /// <summary>How many certificates the reads for exports returned in all, as the database would load them (#367).</summary>
    public int CertificatesReadForExports { get; private set; }

    public Task<bool> ExistsForAttemptAsync(AttemptId attempt, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.Any(certificate => certificate.Attempt == attempt));

    public Task<bool> ExistsAsync(CertificateId id, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.Any(certificate => certificate.Id == id));

    public Task<Option<Certificate>> LoadAsync(CertificateId id, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.FirstOrDefault(certificate => certificate.Id == id) is { } found
            ? Option<Certificate>.Some(found)
            : Option<Certificate>.None);

    public Task<Option<Certificate>> FindAsync(CertificateId id, CancellationToken cancellationToken) => LoadAsync(id, cancellationToken);

    public Task<Option<Certificate>> FindByVerificationCodeAsync(VerificationCode code, CancellationToken cancellationToken)
    {
        VerificationLookups++;
        var found = Stored.SingleOrDefault(certificate => certificate.VerificationCode == code);
        if (found is null)
        {
            return Task.FromResult(Option<Certificate>.None);
        }

        return Task.FromResult(Option<Certificate>.Some(found));
    }

    /// <summary>
    /// The certificates issued from <paramref name="issuedFrom"/> on and before <paramref name="issuedBefore"/> whose holder
    /// had a membership in the pharmacy at the instant of issue (started at or before it, no ending of it at or before it),
    /// and that the pharmacy still keeps at <paramref name="keptAt"/> for <paramref name="keptFor"/>, oldest first, then by id, at most <paramref name="limit"/>,
    /// as the store in Infrastructure reads them; none without a membership store.
    /// </summary>
    public Task<IReadOnlyList<Certificate>> IssuedInPharmacyAsync(PharmacyId pharmacy, Timestamp issuedFrom, Timestamp issuedBefore, Timestamp keptAt, RetentionPeriod keptFor, int limit, CancellationToken cancellationToken)
    {
        var issued = Stored
            .Where(certificate => issuedFrom <= certificate.IssuedAt && certificate.IssuedAt < issuedBefore)
            .Where(certificate => certificate.IsKeptByPharmaciesAt(keptAt, keptFor))
            .Where(certificate => memberships is not null && memberships.Memberships.Any(membership =>
                membership.Pharmacy == pharmacy
                && membership.Person == certificate.Holder
                && membership.StartedAt <= certificate.IssuedAt
                && !memberships.Endings.Any(ending => ending.Id == membership.Id && ending.EndedAt <= certificate.IssuedAt)))
            .OrderBy(certificate => certificate.IssuedAt.Value)
            .ThenBy(certificate => certificate.Id.Value)
            .Take(limit)
            .ToList();
        CertificatesReadForExports += issued.Count;
        return Task.FromResult<IReadOnlyList<Certificate>>(issued);
    }

    /// <summary>
    /// A page of the holder's stored certificates, newest first (issued later, then the larger id), and how many the
    /// holder has in all, as the database orders, skips and counts them.
    /// </summary>
    public Task<(IReadOnlyList<Certificate> Items, int TotalCount)> ListForHolderAsync(PersonId holder, Page page, PageSize pageSize, CancellationToken cancellationToken)
    {
        var holders = Stored
            .Where(certificate => certificate.Holder == holder)
            .OrderByDescending(certificate => certificate.IssuedAt)
            .ThenByDescending(certificate => certificate.Id.Value.ToString("N"), StringComparer.Ordinal)
            .ToList();
        IReadOnlyList<Certificate> items = [.. holders.Skip(page.Skip(pageSize)).Take(pageSize.Value)];
        return Task.FromResult((items, holders.Count));
    }

    public void Add(Certificate certificate) => added.Add(certificate);

    public Task<IReadOnlyList<Certificate>> OfHolderAsync(PersonId holder, CancellationToken cancellationToken)
    {
        Journal?.Add("read-certificates");
        IReadOnlyList<Certificate> holders = [.. Stored.Where(certificate => certificate.Holder == holder)];
        return Task.FromResult(holders);
    }

    public void Remove(Certificate certificate) => removed.Add(certificate);

    public Task PublishAsync(IntegrationEvent integrationEvent)
    {
        queued.Add(integrationEvent);
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Stored.AddRange(added);
        added.Clear();
        Stored.RemoveAll(removed.Contains);
        removed.Clear();
        Published.AddRange(queued);
        queued.Clear();
        Commits++;
        return Task.CompletedTask;
    }

    /// <summary>Forgets what was added, removed and published since the last commit, as a rollback does.</summary>
    public void Discard()
    {
        added.Clear();
        removed.Clear();
        queued.Clear();
    }
}
