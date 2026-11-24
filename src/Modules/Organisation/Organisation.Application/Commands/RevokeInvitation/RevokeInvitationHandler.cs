using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.RevokeInvitation;

/// <summary>Withdraws the open invitation: its link stops working and the address may be invited again.</summary>
public sealed class RevokeInvitationHandler(IPharmacyStore pharmacies, IOrganisationUnitOfWork unitOfWork)
    : ICommandHandler<RevokeInvitation, Result<Unit, ApiError>>
{
    public async Task<Result<Unit, ApiError>> HandleAsync(RevokeInvitation command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var found = await pharmacies.LoadAsync(command.Pharmacy, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(PharmacyFailures.NotFound(command.Pharmacy.Value));
        }

        var pharmacy = found.Value;
        var access = pharmacy.Require(command.By, Capability.ManageMembers);
        if (access.IsFailure)
        {
            return Failure(access.Error);
        }

        var changed = pharmacy.RevokeInvitation(command.Membership);
        if (changed.IsFailure)
        {
            return Failure(PharmacyFailures.From(changed.Error));
        }

        var saved = await unitOfWork.TryCommitAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Failure(PharmacyFailures.LostRace(saved.Error, command.Pharmacy.Value));
        }

        return Success(Unit.Value);
    }
}
