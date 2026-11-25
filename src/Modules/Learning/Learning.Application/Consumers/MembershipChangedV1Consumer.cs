using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;

/// <summary>
/// Stores a change Organisation made to a membership, once per version, whether or not the membership's start has
/// arrived yet: a change proves the membership started (docs/adr/0017). Version 1 is the start, so a change is 2 or more
/// (docs/adr/0015). A change already stored at that version means its event was handled, so a second delivery changes
/// nothing. It deduplicates on the membership and the version, not on <c>Metadata.EventIdentifier</c>, for the reason
/// <see cref="MembershipStartedV1Consumer"/> gives. Copied from Compliance's
/// <c>Compliance.Application/Consumers/MembershipChangedV1Consumer.cs</c>.
/// <para>
/// A message of a person Learning erased (#115) is dropped, logged without the name, and the stored copies of such
/// messages holding the person's id are purged, since it may have been in flight past the erasure's purge. The person is
/// held before the tombstone is read, as the erasure holds them, so neither can slip between the other's check and commit.
/// </para>
/// </summary>
/// <remarks>
/// The handler takes the message and a cancellation token only; everything else comes by constructor, since the
/// AppHost's messaging topology reads every parameter type of a consumer's handler (docs/adr/0011).
/// </remarks>
public sealed partial class MembershipChangedV1Consumer(
    IMembershipCopies memberships,
    IErasedPersonStore erased,
    IPersonalMessagePurge purge,
    ILearningUnitOfWork unitOfWork,
    ILogger<MembershipChangedV1Consumer> logger)
{
    /// <summary>The messages Learning stores that carry a person's name, purged for an erased person whose message is dropped.</summary>
    private static readonly IReadOnlyCollection<Type> PersonalMessages = [typeof(MembershipStartedV1), typeof(MembershipChangedV1)];

    public async Task HandleAsync(MembershipChangedV1 message, CancellationToken cancellationToken)
    {
        // Refused here, each by its name, so a poison message dead-letters with a reason that says which field. Name,
        // job function and role are not kept, so not checked either.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.MembershipId, nameof(message.MembershipId)).IsNotEmpty();
        Ensure.That(message.PersonId, nameof(message.PersonId)).IsNotEmpty();
        Ensure.That(message.PharmacyId, nameof(message.PharmacyId)).IsNotEmpty();
        Ensure.That(message.Version, nameof(message.Version)).AtLeast(2);

        var person = PersonId.From(message.PersonId);
        await erased.HoldAsync(person, cancellationToken);
        if (await erased.IsErasedAsync(person, cancellationToken))
        {
            ErasedPersonDropped(logger, message.MembershipId);
            await purge.PurgeAsync(message.PersonId, PersonalMessages, cancellationToken);
            return;
        }

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

    [LoggerMessage(Level = LogLevel.Information, Message = "Membership {Membership} belongs to an erased person; its event is dropped.")]
    private static partial void ErasedPersonDropped(ILogger logger, Guid membership);
}
