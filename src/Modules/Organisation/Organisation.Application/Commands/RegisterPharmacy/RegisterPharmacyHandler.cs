using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.RegisterPharmacy;

/// <summary>
/// Registers the pharmacy and announces it and the founder's membership to other modules, in the same commit as the
/// pharmacy itself.
/// </summary>
public sealed class RegisterPharmacyHandler(IPharmacyStore pharmacies, IOrganisationUnitOfWork unitOfWork, TimeProvider time)
    : ICommandHandler<RegisterPharmacy, Result<Guid, ApiError>>
{
    public async Task<Result<Guid, ApiError>> HandleAsync(RegisterPharmacy command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        if (!command.FounderEmail.IsVerified)
        {
            return Failure(PharmacyFailures.EmailNotVerified());
        }

        var now = Timestamp.From(time.GetUtcNow());
        var pharmacy = Pharmacy.Register(
            PharmacyId.New(),
            command.Name,
            MembershipId.New(),
            command.Founder,
            command.FounderName,
            command.FounderEmail.Value,
            command.FounderJobFunction,
            now);
        pharmacies.Add(pharmacy);

        foreach (var registered in pharmacy.DomainEvents.OfType<PharmacyRegistered>())
        {
            await unitOfWork.PublishAsync(registered.ToIntegrationEvent());
        }

        foreach (var started in pharmacy.DomainEvents.OfType<MembershipStarted>())
        {
            await unitOfWork.PublishAsync(started.ToIntegrationEvent());
        }

        await unitOfWork.CommitAsync(cancellationToken);
        return Success(pharmacy.Id.Value);
    }
}
