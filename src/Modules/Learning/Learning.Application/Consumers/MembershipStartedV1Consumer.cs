using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;

/// <summary>
/// Stores a membership Organisation started, once, with its person and pharmacy only (docs/adr/0017). A membership
/// starts once, so a member already stored means its event was handled, whatever the event's identifier or payload: a
/// second delivery changes nothing, and the first one wins. It deduplicates on the membership id, not on
/// <c>Metadata.EventIdentifier</c>, since that also absorbs a republish under a new identifier; the primary key backs the
/// check, so of two deliveries racing, the loser's insert fails and its retry finds the row. Copied from Compliance's
/// <c>Compliance.Application/Consumers/MembershipStartedV1Consumer.cs</c>.
/// </summary>
/// <remarks>
/// The handler takes the message and a cancellation token only; everything else comes by constructor, since the
/// AppHost's messaging topology reads every parameter type of a consumer's handler (docs/adr/0011).
/// </remarks>
public sealed class MembershipStartedV1Consumer(
    IMembershipCopies memberships,
    IErasedPersonStore erased,
    IPersonalMessagePurge purge,
    ILearningUnitOfWork unitOfWork,
    ILogger<MembershipStartedV1Consumer> logger)
{
    // Not used yet: the #421 review's red tests (#115) ask for an erased person to be held, checked and dropped, logged
    // without the name, and the dropped message's stored copies purged.
    private readonly IErasedPersonStore erased = erased;
    private readonly IPersonalMessagePurge purge = purge;
    private readonly ILogger<MembershipStartedV1Consumer> logger = logger;

    public async Task HandleAsync(MembershipStartedV1 message, CancellationToken cancellationToken)
    {
        // Refused here, each by its name, so a poison message dead-letters with a reason that says which field. Name,
        // job function and role are not kept, so not checked either.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.MembershipId, nameof(message.MembershipId)).IsNotEmpty();
        Ensure.That(message.PersonId, nameof(message.PersonId)).IsNotEmpty();
        Ensure.That(message.PharmacyId, nameof(message.PharmacyId)).IsNotEmpty();

        if (await memberships.ExistsAsync(MembershipId.From(message.MembershipId), cancellationToken))
        {
            return;
        }

        memberships.Add(new MemberCopy { Id = message.MembershipId, PersonId = message.PersonId, PharmacyId = message.PharmacyId });
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
