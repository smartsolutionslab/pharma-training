using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Retention;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Commands.SetRetentionPeriod;

/// <summary>
/// Sets the pharmacy's retention period (#391, docs/adr/0039), checking in this order, the first that fails answering:
/// the caller is a person (403); the pharmacy id is not empty and the caller has an active membership in it (404); every
/// such membership's current role grants <see cref="Capability.ManageRetention"/> (403, docs/adr/0003); the requested
/// years are a period of 2 to 30 (400). So only a caller who may set the period learns what it may be. The period
/// replaces the one the pharmacy held in one upsert; the last set wins.
/// </summary>
public sealed class SetRetentionPeriodHandler(IMembershipStore memberships, IPharmacyRetentionStore retention)
    : ICommandHandler<SetRetentionPeriod, Result<Unit, ApiError>>
{
    public async Task<Result<Unit, ApiError>> HandleAsync(SetRetentionPeriod command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        if (!command.Caller.HasValue)
        {
            return Failure(CertificateFailures.IdentityIncomplete());
        }

        // The empty Guid is no pharmacy; nobody is a member of it.
        if (!command.Pharmacy.HasValue)
        {
            return Failure(ExportFailures.PharmacyNotFound(Guid.Empty));
        }

        var caller = command.Caller.Value;
        var pharmacy = command.Pharmacy.Value;
        var access = await CertificationAccess.RequireAsync(memberships, caller, pharmacy, Capability.ManageRetention, cancellationToken);
        if (access.IsFailure)
        {
            return Failure(access.Error);
        }

        if (command.Years.Value is not { } years || !RetentionPeriod.TryFrom(years, out var period))
        {
            return Failure(RetentionFailures.InvalidPeriod());
        }

        await retention.SetAsync(PharmacyRetention.Chosen(pharmacy, period), cancellationToken);
        return Success(Unit.Value);
    }
}
