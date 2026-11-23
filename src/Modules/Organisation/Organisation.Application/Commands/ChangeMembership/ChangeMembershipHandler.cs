using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.ChangeMembership;

/// <summary>
/// Changes the membership's job function and role and announces it (MembershipChangedV1) in the same commit; a save
/// that lost a race saves and announces nothing (docs/adr/0002, docs/adr/0015).
/// </summary>
public sealed class ChangeMembershipHandler(IPharmacyStore pharmacies, IOrganisationUnitOfWork unitOfWork)
    : ICommandHandler<ChangeMembership, Result<Unit, ApiError>>
{
    public async Task<Result<Unit, ApiError>> HandleAsync(ChangeMembership command, CancellationToken cancellationToken)
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

        var changed = pharmacy.ChangeMembership(command.Membership, command.JobFunction, command.Role);
        if (changed.IsFailure)
        {
            return Failure(PharmacyFailures.From(changed.Error));
        }

        foreach (var change in pharmacy.DomainEvents.OfType<MembershipChanged>())
        {
            await unitOfWork.PublishAsync(change.ToIntegrationEvent());
        }

        if (!await unitOfWork.TryCommitAsync(cancellationToken))
        {
            return Failure(PharmacyFailures.Changed(command.Pharmacy.Value));
        }

        return Success(Unit.Value);
    }
}
