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
/// their own commit, as the erasure holds them, and the PDFs go before the rows. A person whose purge fails is
/// logged by their id and abandoned (<see cref="ICertificationUnitOfWork.AbandonAsync"/>, which rolls back their
/// hold), and the pass goes on with the next; the next pass tries them again. Nothing is published.
/// </summary>
public sealed partial class PurgeErasedCertificatesHandler(
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
    private readonly ILogger<PurgeErasedCertificatesHandler> logger = logger;

    public async Task<Result<Unit, ApiError>> HandleAsync(PurgeErasedCertificates command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var now = Timestamp.From(time.GetUtcNow());
        var holders = await erased.HoldingCertificatesAsync(cancellationToken);
        foreach (var person in holders)
        {
            try
            {
                await PurgeAsync(person, now, cancellationToken);
            }
            catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
            {
                // One person whose purge keeps failing must not hold up everyone after them (#446 review): their work is
                // given up, with their hold, and the next pass tries them again. Logged by their id alone.
                PurgeFailed(logger, person.Value, exception);
                await unitOfWork.AbandonAsync(cancellationToken);
            }
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

        // Committed whether or not anything went: the commit also ends the hold's transaction. One of several commits in
        // this scope, one per person, so Wolverine does not warn at each flush after the first (#462).
        await unitOfWork.CommitOneOfSeveralAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Purging the certificates of erased person {Person} failed; it is given up for this pass and tried again in the next.")]
    private static partial void PurgeFailed(ILogger logger, Guid person, Exception exception);
}
