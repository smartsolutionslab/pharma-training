using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Consumers;

/// <summary>Erases a person in Certification (#115). Not implemented yet: slice 4's red tests.</summary>
public sealed class PersonErasureRequestedV1Consumer(
    ICertificateStore certificates,
    IMembershipStore memberships,
    IErasedPersonStore erased,
    IBlobDeletes blobs,
    IPersonalMessagePurge purge,
    ICertificationUnitOfWork unitOfWork,
    TimeProvider time)
{
    private readonly ICertificateStore certificates = certificates;
    private readonly IMembershipStore memberships = memberships;
    private readonly IErasedPersonStore erased = erased;
    private readonly IBlobDeletes blobs = blobs;
    private readonly IPersonalMessagePurge purge = purge;
    private readonly ICertificationUnitOfWork unitOfWork = unitOfWork;
    private readonly TimeProvider time = time;

    public Task HandleAsync(PersonErasureRequestedV1 message, CancellationToken cancellationToken) =>
        throw new NotImplementedException(
            $"{certificates.GetType().Name}, {memberships.GetType().Name}, {erased.GetType().Name}, {blobs.GetType().Name}, {purge.GetType().Name}, {unitOfWork.GetType().Name}, {time.GetType().Name}");
}
