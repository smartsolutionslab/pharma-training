using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.RenewInvitation;

/// <summary>Gives the open or expired invitation a fresh token valid for 14 more days, so the old link stops working. In the same commit it requests the new mail (InvitationMailRequestedV1), which the consumer sends.</summary>
public sealed class RenewInvitationHandler(IPharmacyStore pharmacies, IOrganisationUnitOfWork unitOfWork, TimeProvider time)
    : ICommandHandler<RenewInvitation, Result<Unit, ApiError>>
{
    public async Task<Result<Unit, ApiError>> HandleAsync(RenewInvitation command, CancellationToken cancellationToken)
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

        var changed = pharmacy.RenewInvitation(command.Membership, Timestamp.From(time.GetUtcNow()));
        if (changed.IsFailure)
        {
            return Failure(PharmacyFailures.From(changed.Error));
        }

        foreach (var requested in pharmacy.DomainEvents.OfType<MemberInvited>())
        {
            // Saved with the invitation in the same commit; the consumer issues the link and mails it (docs/adr/0004).
            await unitOfWork.PublishAsync(requested.ToIntegrationEvent());
        }

        if (!await unitOfWork.TryCommitAsync(cancellationToken))
        {
            return Failure(PharmacyFailures.Changed(command.Pharmacy.Value));
        }

        return Success(Unit.Value);
    }
}
