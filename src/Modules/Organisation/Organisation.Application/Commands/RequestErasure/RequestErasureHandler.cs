using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Erasures;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.RequestErasure;

/// <summary>
/// Ends the person's active memberships and revokes the open invitations to their addresses, refused while they are the
/// last active manager of a pharmacy; logs the erasure and requests its confirmation mail, all in one commit. Every
/// pharmacy is checked before any is changed, so a refusal leaves each of them as it was. Names and addresses stay until
/// the confirmation is mailed (<see cref="Consumers.ErasureConfirmationMailRequestedV1Consumer"/>).
/// </summary>
public sealed class RequestErasureHandler(IPharmacyStore pharmacies, IErasureStore erasures, IOrganisationUnitOfWork unitOfWork, TimeProvider time)
    : ICommandHandler<RequestErasure, Result<Guid, ApiError>>
{
    private readonly IPharmacyStore pharmacies = pharmacies;
    private readonly IErasureStore erasures = erasures;
    private readonly IOrganisationUnitOfWork unitOfWork = unitOfWork;
    private readonly TimeProvider time = time;

    public async Task<Result<Guid, ApiError>> HandleAsync(RequestErasure command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var addresses = await pharmacies.AddressesOfAsync(command.Person, cancellationToken);
        var touched = await pharmacies.LoadTouchingPersonAsync(command.Person, addresses, cancellationToken);

        foreach (var pharmacy in touched)
        {
            var releasable = pharmacy.MayReleaseForErasure(command.Person);
            if (releasable.IsFailure)
            {
                return Failure(Refusal(releasable.Error));
            }
        }

        var now = Timestamp.From(time.GetUtcNow());
        foreach (var pharmacy in touched)
        {
            var released = pharmacy.ReleaseForErasure(command.Person, addresses, now);
            if (released.IsFailure)
            {
                return Failure(Refusal(released.Error));
            }
        }

        foreach (var membershipEnded in touched.SelectMany(pharmacy => pharmacy.DomainEvents.OfType<MembershipEnded>()))
        {
            await unitOfWork.PublishAsync(membershipEnded.ToIntegrationEvent());
        }

        var erasure = Erasure.Request(ErasureId.New(), command.By, now);
        erasures.Add(erasure);
        await unitOfWork.PublishAsync(new ErasureConfirmationMailRequestedV1(erasure.Id.Value, command.Person.Value));

        var saved = await unitOfWork.TryCommitAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Failure(ErasureFailures.LostRace(saved.Error));
        }

        return Success(erasure.Id.Value);
    }

    /// <summary>The last-manager refusal is the erasure's own; any other refusal is mapped as the pharmacies map it.</summary>
    private static ApiError Refusal(DomainError error)
    {
        if (error is LastManagerCannotLeave)
        {
            return ErasureFailures.LastManager();
        }

        return PharmacyFailures.From(error);
    }
}
