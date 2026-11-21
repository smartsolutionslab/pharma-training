using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.RenewInvitation;

/// <summary>Stub for #9's red tests (T4): answers success without renewing anything.</summary>
public sealed class RenewInvitationHandler(IPharmacyStore pharmacies, IOrganisationUnitOfWork unitOfWork, TimeProvider time)
    : ICommandHandler<RenewInvitation, Result<Unit, ApiError>>
{
    public Task<Result<Unit, ApiError>> HandleAsync(RenewInvitation command, CancellationToken cancellationToken)
    {
        Ensure.That(pharmacies).IsNotNull();
        Ensure.That(unitOfWork).IsNotNull();
        Ensure.That(time).IsNotNull();
        return Task.FromResult<Result<Unit, ApiError>>(Success(Unit.Value));
    }
}
