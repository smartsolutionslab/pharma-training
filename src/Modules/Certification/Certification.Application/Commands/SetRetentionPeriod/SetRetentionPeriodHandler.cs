using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Commands.SetRetentionPeriod;

// stub for #391
public sealed class SetRetentionPeriodHandler(IMembershipStore memberships, IPharmacyRetentionStore retention)
    : ICommandHandler<SetRetentionPeriod, Result<Unit, ApiError>>
{
    public Task<Result<Unit, ApiError>> HandleAsync(SetRetentionPeriod command, CancellationToken cancellationToken)
    {
        _ = memberships;
        _ = retention;
        throw new NotImplementedException();
    }
}
