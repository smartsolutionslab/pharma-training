using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.InviteMember;

/// <summary>Stub for #9's red tests (T3): answers success without inviting anyone.</summary>
public sealed class InviteMemberHandler(IPharmacyStore pharmacies, IOrganisationUnitOfWork unitOfWork, TimeProvider time)
    : ICommandHandler<InviteMember, Result<Guid, ApiError>>
{
    public Task<Result<Guid, ApiError>> HandleAsync(InviteMember command, CancellationToken cancellationToken)
    {
        Ensure.That(pharmacies).IsNotNull();
        Ensure.That(unitOfWork).IsNotNull();
        Ensure.That(time).IsNotNull();
        return Task.FromResult<Result<Guid, ApiError>>(Success(Guid.Empty));
    }
}
