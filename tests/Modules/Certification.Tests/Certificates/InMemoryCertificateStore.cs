using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Tests.Certificates;

/// <summary>
/// The certificates table in memory, and the module's unit of work over it: what is added is stored, and what is
/// published is sent, only once committed, as the outbox sends it in the same transaction. A certificate loaded is the
/// stored one itself, as EF Core tracks it, so what is changed on it is what a commit saves.
/// </summary>
internal sealed class InMemoryCertificateStore : ICertificateStore, ICertificationUnitOfWork
{
    private readonly List<Certificate> added = [];
    private readonly List<IntegrationEvent> queued = [];

    public List<Certificate> Stored { get; } = [];

    public List<IntegrationEvent> Published { get; } = [];

    public int Commits { get; private set; }

    /// <summary>How often a certificate was looked up by its verification code.</summary>
    public int VerificationLookups { get; private set; }

    public Task<bool> ExistsForAttemptAsync(AttemptId attempt, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.Any(certificate => certificate.Attempt == attempt));

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

    public void Add(Certificate certificate) => added.Add(certificate);

    public Task PublishAsync(IntegrationEvent integrationEvent)
    {
        queued.Add(integrationEvent);
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken cancellationToken)
    {
        Stored.AddRange(added);
        added.Clear();
        Published.AddRange(queued);
        queued.Clear();
        Commits++;
        return Task.CompletedTask;
    }
}
