using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.EndMembership;

/// <summary>Ends the membership and announces it to other modules as MembershipEndedV1, in the same commit.</summary>
public sealed class EndMembershipHandler(IPharmacyStore pharmacies, IOrganisationUnitOfWork unitOfWork, TimeProvider time)
    : ICommandHandler<EndMembership, Result<Unit, ApiError>>
{
    public async Task<Result<Unit, ApiError>> HandleAsync(EndMembership command, CancellationToken cancellationToken)
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

        var ended = pharmacy.EndMembership(command.Membership, Timestamp.From(time.GetUtcNow()));
        if (ended.IsFailure)
        {
            return Failure(PharmacyFailures.From(ended.Error));
        }

        foreach (var membershipEnded in pharmacy.DomainEvents.OfType<MembershipEnded>())
        {
            await unitOfWork.PublishAsync(membershipEnded.ToIntegrationEvent());
        }

        if (!await unitOfWork.TryCommitAsync(cancellationToken))
        {
            return Failure(PharmacyFailures.Changed(command.Pharmacy.Value));
        }

        return Success(Unit.Value);
    }
}
