using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Erasures;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Commands.PurgeErasedCertificates;

/// <summary>
/// Deletes the retained certificates of erased persons that no covering pharmacy keeps any longer under its current
/// retention period (#115, slice 5, the owner's decision of 25 November 2026): the rule the erasure applied
/// (<see cref="ErasedCertificates"/>), applied again to each tombstoned person who still holds a certificate. A period
/// lengthened after a deletion does not bring the certificate back. Each person is held from the first read of theirs to
/// their own commit, as the erasure holds them, and the PDFs go before the rows. A person whose purge fails throws: the
/// pass ends there, and the next one finds what is left. Nothing is published.
/// </summary>
public sealed class PurgeErasedCertificatesHandler(
    IErasedPersonStore erased,
    ICertificateStore certificates,
    IMembershipStore memberships,
    IPharmacyRetentionStore retention,
    IBlobDeletes blobs,
    ICertificationUnitOfWork unitOfWork,
    TimeProvider time,
    ILogger<PurgeErasedCertificatesHandler> logger)
    : ICommandHandler<PurgeErasedCertificates, Result<Unit, ApiError>>
{
    private readonly IErasedPersonStore erased = erased;
    private readonly ICertificateStore certificates = certificates;
    private readonly IMembershipStore memberships = memberships;
    private readonly IPharmacyRetentionStore retention = retention;
    private readonly IBlobDeletes blobs = blobs;
    private readonly ICertificationUnitOfWork unitOfWork = unitOfWork;
    private readonly TimeProvider time = time;

    public async Task<Result<Unit, ApiError>> HandleAsync(PurgeErasedCertificates command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();
        Ensure.That(logger).IsNotNull();

        var now = Timestamp.From(time.GetUtcNow());
        var holders = await erased.HoldingCertificatesAsync(cancellationToken);
        foreach (var person in holders)
        {
            await PurgeAsync(person, now, cancellationToken);
        }

        return Success(Unit.Value);
    }

    private async Task PurgeAsync(PersonId person, Timestamp now, CancellationToken cancellationToken)
    {
        // Held before anything of the person is read, as the erasure, membership, passed-test and render consumers hold
        // them, so none of them slips a certificate or a PDF between these reads and the deletes.
        await erased.HoldAsync(person, cancellationToken);

        var held = await certificates.OfHolderAsync(person, cancellationToken);
        var persons = await memberships.OfPersonAsync(person, cancellationToken);
        var endings = await memberships.EndingsOfPersonAsync(person, cancellationToken);

        var removed = await ErasedCertificates.NoLongerKeptAsync(held, persons, endings, retention, now, cancellationToken);
        if (removed.Count > 0)
        {
            await ErasedCertificates.RemoveAsync(removed, blobs, certificates, cancellationToken);
        }

        // Committed whether or not anything went: the commit also ends the hold's transaction.
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
