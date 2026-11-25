using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.RequestErasure;

/// <summary>
/// Ends the person's active memberships and revokes the open invitations to their addresses, refused while they are the
/// last active manager of a pharmacy; logs the erasure and requests its confirmation mail, all in one commit.
/// </summary>
public sealed class RequestErasureHandler(IPharmacyStore pharmacies, IErasureStore erasures, IOrganisationUnitOfWork unitOfWork, TimeProvider time)
    : ICommandHandler<RequestErasure, Result<Guid, ApiError>>
{
    private readonly IPharmacyStore pharmacies = pharmacies;
    private readonly IErasureStore erasures = erasures;
    private readonly IOrganisationUnitOfWork unitOfWork = unitOfWork;
    private readonly TimeProvider time = time;

    public Task<Result<Guid, ApiError>> HandleAsync(RequestErasure command, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
