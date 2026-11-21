using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.RevokeInvitation;

/// <summary>Stub for #9's red tests (T4): answers success without revoking anything.</summary>
public sealed class RevokeInvitationHandler(IPharmacyStore pharmacies, IOrganisationUnitOfWork unitOfWork)
    : ICommandHandler<RevokeInvitation, Result<Unit, ApiError>>
{
    public Task<Result<Unit, ApiError>> HandleAsync(RevokeInvitation command, CancellationToken cancellationToken)
    {
        Ensure.That(pharmacies).IsNotNull();
        Ensure.That(unitOfWork).IsNotNull();
        return Task.FromResult<Result<Unit, ApiError>>(Success(Unit.Value));
    }
}
