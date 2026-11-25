using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Erasures;

/// <summary>
/// Certification's unit of work over the three stores an erasure touches: a commit saves the tombstones, the memberships
/// and the certificates (the removed ones go) and sends what was published, in the certificate store's
/// <see cref="InMemoryCertificateStore.Published"/>; it is written down in the journal as <c>commit</c>.
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

    public Task PublishAsync(IntegrationEvent integrationEvent) => certificates.PublishAsync(integrationEvent);

    public async Task CommitAsync(CancellationToken cancellationToken)
    {
        journal.Add("commit");
        erased.Commit();
        await memberships.CommitAsync(cancellationToken);
        await certificates.CommitAsync(cancellationToken);
        Commits++;
    }
}
