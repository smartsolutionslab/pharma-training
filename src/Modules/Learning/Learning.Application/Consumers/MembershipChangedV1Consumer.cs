using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;

/// <summary>
/// Stores a change Organisation made to a membership, once per version, whether or not the membership's start has
/// arrived yet: a change proves the membership started (docs/adr/0017). Version 1 is the start, so a change is 2 or more
/// (docs/adr/0015). A change already stored at that version means its event was handled, so a second delivery changes
/// nothing. It deduplicates on the membership and the version, not on <c>Metadata.EventIdentifier</c>, for the reason
/// <see cref="MembershipStartedV1Consumer"/> gives. Copied from Compliance's
/// <c>Compliance.Application/Consumers/MembershipChangedV1Consumer.cs</c>.
/// </summary>
/// <remarks>
/// The handler takes the message and a cancellation token only; everything else comes by constructor, since the
/// AppHost's messaging topology reads every parameter type of a consumer's handler (docs/adr/0011).
/// </remarks>
public sealed class MembershipChangedV1Consumer(
    IMembershipCopies memberships,
    IErasedPersonStore erased,
    IPersonalMessagePurge purge,
    ILearningUnitOfWork unitOfWork,
    ILogger<MembershipChangedV1Consumer> logger)
{
    // Not used yet: the #421 review's red tests (#115) ask for an erased person to be held, checked and dropped, logged
    // without the name, and the dropped message's stored copies purged.
    private readonly IErasedPersonStore erased = erased;
    private readonly IPersonalMessagePurge purge = purge;
    private readonly ILogger<MembershipChangedV1Consumer> logger = logger;

    public async Task HandleAsync(MembershipChangedV1 message, CancellationToken cancellationToken)
    {
        // Refused here, each by its name, so a poison message dead-letters with a reason that says which field. Name,
        // job function and role are not kept, so not checked either.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.MembershipId, nameof(message.MembershipId)).IsNotEmpty();
        Ensure.That(message.PersonId, nameof(message.PersonId)).IsNotEmpty();
        Ensure.That(message.PharmacyId, nameof(message.PharmacyId)).IsNotEmpty();
        Ensure.That(message.Version, nameof(message.Version)).AtLeast(2);

        if (await memberships.HasChangeAsync(MembershipId.From(message.MembershipId), MembershipVersion.From(message.Version), cancellationToken))
        {
            return;
        }

        memberships.Add(new MembershipChangeCopy
        {
            MembershipId = message.MembershipId,
            Version = message.Version,
            PersonId = message.PersonId,
            PharmacyId = message.PharmacyId,
        });
        await unitOfWork.CommitAsync(cancellationToken);
    }
}
