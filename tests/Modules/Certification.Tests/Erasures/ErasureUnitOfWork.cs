using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Erasures;

/// <summary>
/// Certification's unit of work over the three stores an erasure touches: a commit saves the tombstones, the memberships
/// and the certificates (the removed ones go) and sends what was published, in the certificate store's
/// <see cref="InMemoryCertificateStore.Published"/>; it is written down in the journal as <c>commit</c>. Abandoning forgets what the three stores hold unsaved and is
/// written down as <c>abandon</c>. Which method each commit came through is recorded in <see cref="CommitMethods"/>:
/// <see cref="CommitOneOfSeveralAsync"/> commits as <see cref="CommitAsync"/> does, and is a public member of its own, so it
/// implements the interface's method once the interface declares it (#462).
/// </summary>
internal sealed class ErasureUnitOfWork(
    InMemoryCertificateStore certificates,
    InMemoryMembershipStore memberships,
    InMemoryErasedPersonStore erased,
    List<string> journal) : ICertificationUnitOfWork
{
    private readonly InMemoryCertificateStore certificates = certificates;
    private readonly InMemoryMembershipStore memberships = memberships;
    private readonly InMemoryErasedPersonStore erased = erased;
    private readonly List<string> journal = journal;

    public int Commits { get; private set; }

    /// <summary>The method each commit came through, in order: <c>CommitAsync</c> or <c>CommitOneOfSeveralAsync</c>.</summary>
    public List<string> CommitMethods { get; } = [];

    public Task PublishAsync(IntegrationEvent integrationEvent) => certificates.PublishAsync(integrationEvent);

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        CommitMethods.Add(nameof(CommitAsync));
        return SaveAsync(cancellationToken);
    }

    public Task CommitOneOfSeveralAsync(CancellationToken cancellationToken)
    {
        CommitMethods.Add(nameof(CommitOneOfSeveralAsync));
        return SaveAsync(cancellationToken);
    }

    public Task AbandonAsync(CancellationToken cancellationToken)
    {
        journal.Add("abandon");
        erased.Discard();
        memberships.Discard();
        certificates.Discard();
        return Task.CompletedTask;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        journal.Add("commit");
        erased.Commit();
        await memberships.CommitAsync(cancellationToken);
        await certificates.CommitAsync(cancellationToken);
        Commits++;
    }
}
