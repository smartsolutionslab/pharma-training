using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Retention;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Commands.SetRetentionPeriod;

/// <summary>
/// Sets the pharmacy's retention period (#391, docs/adr/0039) for a caller whose membership in the pharmacy grants
/// <see cref="Capability.ManageRetention"/> (docs/adr/0003): a non-member is told the pharmacy is not found, a member
/// without the capability is forbidden. The period replaces the one the pharmacy held in one upsert; the last set wins.
/// </summary>
public sealed class SetRetentionPeriodHandler(IMembershipStore memberships, IPharmacyRetentionStore retention)
    : ICommandHandler<SetRetentionPeriod, Result<Unit, ApiError>>
{
    public async Task<Result<Unit, ApiError>> HandleAsync(SetRetentionPeriod command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var access = await CertificationAccess.RequireAsync(memberships, command.Caller, command.Pharmacy, Capability.ManageRetention, cancellationToken);
        if (access.IsFailure)
        {
            return Failure(access.Error);
        }

        await retention.SetAsync(PharmacyRetention.Chosen(command.Pharmacy, command.Period), cancellationToken);
        return Success(Unit.Value);
    }
}
