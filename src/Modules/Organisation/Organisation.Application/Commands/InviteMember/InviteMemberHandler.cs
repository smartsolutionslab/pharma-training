using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.InviteMember;

/// <summary>
/// Invites the address and, in the same commit, requests its mail (InvitationMailRequestedV1) on the host's durable
/// local queue; the consumer issues the link and mails it, so nothing after the commit depends on the request. Beyond
/// the pharmacy's daily cap of invitation mails it answers 429 with the instant it may invite again.
/// </summary>
public sealed class InviteMemberHandler(IPharmacyStore pharmacies, IOrganisationUnitOfWork unitOfWork, TimeProvider time, InvitationMailLimits limits, ILogger<InviteMemberHandler> logger)
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

        var now = Timestamp.From(time.GetUtcNow());
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

        foreach (var requested in pharmacy.DomainEvents.OfType<MemberInvited>())
        {
            // Saved with the invitation in the same commit; the consumer issues the link and mails it (docs/adr/0004).
            await unitOfWork.PublishAsync(requested.ToIntegrationEvent());
        }

        if (!await unitOfWork.TryCommitAsync(cancellationToken))
        {
            return Failure(PharmacyFailures.Changed(command.Pharmacy.Value));
        }

        return Success(invited.Value.Value);
    }
}
