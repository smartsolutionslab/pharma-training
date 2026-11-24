using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.RenewInvitation;

/// <summary>Gives the open or expired invitation a fresh token valid for 14 more days, so the old link stops working. In the same commit it requests the new mail (InvitationMailRequestedV1), which the consumer sends. Within the invitation's cooldown, beyond the pharmacy's daily cap of invitation mails, within the address's cooldown (whichever pharmacy mailed it) or beyond the renewing manager's daily cap across their pharmacies it answers 429 with the instant it may renew, in that order, and commits nothing.</summary>
public sealed class RenewInvitationHandler(
    IPharmacyStore pharmacies,
    IInvitationRecipientStore recipients,
    IInvitationSenderStore senders,
    IOrganisationUnitOfWork unitOfWork,
    IDatabaseClock clock,
    InvitationMailLimits limits,
    RecipientAndSenderMailLimits recipientAndSenderLimits,
    ILogger<RenewInvitationHandler> logger)
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

        // The whole operation (its mail limits, its request time and expiry) runs on the database's clock, which every
        // instance shares, so a clock days apart cannot lift or lock the limits (docs/adr/0004).
        var now = await clock.NowAsync(cancellationToken);
        InvitationMailClock.WarnWhenAhead(logger, pharmacy, now);
        var changed = pharmacy.RenewInvitation(command.Membership, now, limits);
        if (changed.IsFailure)
        {
            return Failure(PharmacyFailures.From(changed.Error));
        }

        // Counted on the renewed membership's address and on the manager who renews, in the same commit as the pharmacy
        // (docs/adr/0004).
        var renewed = pharmacy.Memberships.Single(membership => membership.Id == command.Membership);
        var counted = await InvitationMailCounting.CountAsync(recipients, senders, recipientAndSenderLimits, logger, renewed.Email, command.By, now, cancellationToken);
        if (counted.IsFailure)
        {
            return Failure(counted.Error);
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
