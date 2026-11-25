using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.InviteMember;

/// <summary>
/// Invites the address and, in the same commit, requests its mail (InvitationMailRequestedV1) on the host's durable
/// local queue; the consumer issues the link and mails it, so nothing after the commit depends on the request. Beyond
/// the pharmacy's daily cap of invitation mails, within the address's cooldown (whichever pharmacy mailed it) or beyond
/// the sender's daily cap across their pharmacies it answers 429 with the instant it may invite again, in that order,
/// and commits nothing.
/// </summary>
public sealed class InviteMemberHandler(
    IPharmacyStore pharmacies,
    IInvitationRecipientStore recipients,
    IInvitationSenderStore senders,
    IOrganisationUnitOfWork unitOfWork,
    IDatabaseClock clock,
    InvitationMailLimits limits,
    RecipientAndSenderMailLimits recipientAndSenderLimits,
    ILogger<InviteMemberHandler> logger)
    : ICommandHandler<InviteMember, Result<Guid, ApiError>>
{
    public async Task<Result<Guid, ApiError>> HandleAsync(InviteMember command, CancellationToken cancellationToken)
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
        var invited = pharmacy.Invite(
            MembershipId.New(),
            command.Email,
            command.JobFunction,
            command.Role,
            now,
            limits);
        if (invited.IsFailure)
        {
            return Failure(PharmacyFailures.From(invited.Error));
        }

        // Counted on the address and the sender in the same commit as the pharmacy (docs/adr/0004).
        var counted = await InvitationMailCounting.CountAsync(recipients, senders, recipientAndSenderLimits, logger, command.Pharmacy, command.Email, command.By, now, cancellationToken);
        if (counted.IsFailure)
        {
            return Failure(counted.Error);
        }

        foreach (var requested in pharmacy.DomainEvents.OfType<MemberInvited>())
        {
            // Saved with the invitation in the same commit; the consumer issues the link and mails it (docs/adr/0004).
            await unitOfWork.PublishAsync(requested.ToIntegrationEvent());
        }

        var saved = await unitOfWork.TryCommitAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Failure(PharmacyFailures.LostRace(saved.Error, command.Pharmacy.Value));
        }

        return Success(invited.Value.Value);
    }
}
