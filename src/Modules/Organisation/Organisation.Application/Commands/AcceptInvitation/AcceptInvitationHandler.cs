using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.AcceptInvitation;

/// <summary>
/// Finds the pharmacy whose open invitation holds the token's hash, starts the membership and announces it
/// (MembershipStartedV1) in the same commit. The link dies with that commit; a save that lost a race saves and
/// announces nothing (docs/adr/0002, docs/adr/0004).
/// </summary>
public sealed class AcceptInvitationHandler(IPharmacyStore pharmacies, IOrganisationUnitOfWork unitOfWork, TimeProvider time)
    : ICommandHandler<AcceptInvitation, Result<Guid, ApiError>>
{
    public async Task<Result<Guid, ApiError>> HandleAsync(AcceptInvitation command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var found = await pharmacies.LoadByInvitationAsync(InvitationTokenHash.Of(command.Token), cancellationToken);
        if (!found.HasValue)
        {
            return Failure(PharmacyFailures.InvitationLinkNotFound());
        }

        var pharmacy = found.Value;
        var accepted = pharmacy.AcceptInvitation(command.Token, command.Person, command.Name, command.Email, Timestamp.From(time.GetUtcNow()));
        if (accepted.IsFailure)
        {
            return Failure(PharmacyFailures.From(accepted.Error));
        }

        foreach (var started in pharmacy.DomainEvents.OfType<MembershipStarted>())
        {
            await unitOfWork.PublishAsync(started.ToIntegrationEvent());
        }

        if (!await unitOfWork.TryCommitAsync(cancellationToken))
        {
            return Failure(PharmacyFailures.Changed(pharmacy.Id.Value));
        }

        return Success(pharmacy.Id.Value);
    }
}
