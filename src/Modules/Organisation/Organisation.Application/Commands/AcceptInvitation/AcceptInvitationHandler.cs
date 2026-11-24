using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.AcceptInvitation;

/// <summary>
/// Finds the pharmacy whose open invitation holds the token's hash, starts the membership and announces it
/// (MembershipStartedV1) in the same commit. The link dies with that commit; a save that lost a race saves and
/// announces nothing (docs/adr/0002, docs/adr/0004). The expiry is checked on the database's clock, which stamped it, so
/// a host whose clock is off neither accepts a run-out invitation nor refuses an open one; that instant also starts the
/// membership.
/// </summary>
public sealed class AcceptInvitationHandler(IPharmacyStore pharmacies, IOrganisationUnitOfWork unitOfWork, IDatabaseClock clock)
    : ICommandHandler<AcceptInvitation, Result<Guid, ApiError>>
{
    public async Task<Result<Guid, ApiError>> HandleAsync(AcceptInvitation command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        // Before the link is looked up, so an unverified account learns nothing about it.
        if (!command.Email.IsVerified)
        {
            return Failure(PharmacyFailures.EmailNotVerified());
        }

        var found = await pharmacies.LoadByInvitationAsync(InvitationTokenHash.Of(command.Token), cancellationToken);
        if (!found.HasValue)
        {
            return Failure(PharmacyFailures.InvitationLinkNotFound());
        }

        // Read only once the link is found, so an unverified account or an unknown link costs no clock read.
        var now = await clock.NowAsync(cancellationToken);
        var pharmacy = found.Value;
        var accepted = pharmacy.AcceptInvitation(command.Token, command.Person, command.Name, command.Email.Value, now);
        if (accepted.IsFailure)
        {
            return Failure(PharmacyFailures.From(accepted.Error));
        }

        foreach (var started in pharmacy.DomainEvents.OfType<MembershipStarted>())
        {
            await unitOfWork.PublishAsync(started.ToIntegrationEvent());
        }

        var saved = await unitOfWork.TryCommitAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Failure(PharmacyFailures.InvitationLostRace(saved.Error));
        }

        return Success(pharmacy.Id.Value);
    }
}
