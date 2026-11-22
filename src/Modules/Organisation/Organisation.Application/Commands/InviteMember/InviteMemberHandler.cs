using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.InviteMember;

/// <summary>
/// Invites the address with a fresh token; the pharmacy keeps only its hash. The mail with the link goes out after the
/// commit, in the reaction to MemberInvited.
/// </summary>
public sealed class InviteMemberHandler(IPharmacyStore pharmacies, IOrganisationUnitOfWork unitOfWork, TimeProvider time)
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

        var invited = pharmacy.Invite(
            MembershipId.New(),
            command.Email,
            InvitationTokens.Next(),
            command.JobFunction,
            command.Role,
            Timestamp.From(time.GetUtcNow()));
        if (invited.IsFailure)
        {
            return Failure(PharmacyFailures.From(invited.Error));
        }

        if (!await unitOfWork.TryCommitAsync(cancellationToken))
        {
            return Failure(PharmacyFailures.Changed(command.Pharmacy.Value));
        }

        return Success(invited.Value.Value);
    }
}
