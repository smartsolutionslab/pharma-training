using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Retention;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Commands.SetRetentionPeriod;

/// <summary>
/// Sets the pharmacy's retention period (#391, docs/adr/0039), checking in this order, the first that fails answering:
/// the caller is a person (403); the pharmacy id is not empty and the caller has an active membership in it (404); every
/// such membership's current role grants <see cref="Capability.ManageRetention"/> (403, docs/adr/0003); the requested
/// years are a period of 2 to 30 (400). So only a caller who may set the period learns what it may be. The period
/// replaces the one the pharmacy held in one upsert; the last set wins. A period that differs from the one stored is
/// logged with the pharmacy, both periods and the caller's person id (#422); a refused or unchanged one is not.
/// </summary>
public sealed partial class SetRetentionPeriodHandler(
    IMembershipStore memberships,
    IPharmacyRetentionStore retention,
    ILogger<SetRetentionPeriodHandler> logger)
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

        // Read for the log only; the upsert below still replaces whatever is stored, so the last set wins.
        var previous = (await retention.FindAsync(pharmacy, cancellationToken))
            .Match<int?>(kept => kept.Period.Value, () => null);

        await retention.SetAsync(PharmacyRetention.Chosen(pharmacy, period), cancellationToken);

        if (previous != period.Value)
        {
            PeriodSet(logger, pharmacy.Value, previous, period.Value, caller.Value);
        }

        return Success(Unit.Value);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Pharmacy {Pharmacy} keeps certificates for {Years} years, {PreviousYears} before; set by person {Person}.")]
    private static partial void PeriodSet(ILogger logger, Guid pharmacy, int? previousYears, int years, Guid person);
}
