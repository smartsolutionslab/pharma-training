using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Commands.PurgeErasedCertificates;

/// <summary>Not implemented yet (#115, slice 5): the red tests describe what it does.</summary>
public sealed class PurgeErasedCertificatesHandler(
    IErasedPersonStore erased,
    ICertificateStore certificates,
    IMembershipStore memberships,
    IPharmacyRetentionStore retention,
    IBlobDeletes blobs,
    ICertificationUnitOfWork unitOfWork,
    TimeProvider time)
    : ICommandHandler<PurgeErasedCertificates, Result<Unit, ApiError>>
{
    private readonly IErasedPersonStore erased = erased;
    private readonly ICertificateStore certificates = certificates;
    private readonly IMembershipStore memberships = memberships;
    private readonly IPharmacyRetentionStore retention = retention;
    private readonly IBlobDeletes blobs = blobs;
    private readonly ICertificationUnitOfWork unitOfWork = unitOfWork;
    private readonly TimeProvider time = time;

    public Task<Result<Unit, ApiError>> HandleAsync(PurgeErasedCertificates command, CancellationToken cancellationToken) =>
        throw new NotImplementedException(
            $"Purging erased persons' certificates is not implemented yet ({erased}, {certificates}, {memberships}, {retention}, {blobs}, {unitOfWork}, {time}).");
}
