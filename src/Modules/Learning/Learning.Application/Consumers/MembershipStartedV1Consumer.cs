using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;
using SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.Consumers;

/// <summary>
/// Stores a membership Organisation started, once, with its person and pharmacy only (docs/adr/0017). A membership
/// starts once, so a member already stored means its event was handled, whatever the event's identifier or payload: a
/// second delivery changes nothing, and the first one wins. It deduplicates on the membership id, not on
/// <c>Metadata.EventIdentifier</c>, since that also absorbs a republish under a new identifier; the primary key backs the
/// check, so of two deliveries racing, the loser's insert fails and its retry finds the row. Copied from Compliance's
/// <c>Compliance.Application/Consumers/MembershipStartedV1Consumer.cs</c>.
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
public sealed partial class MembershipStartedV1Consumer(
    IMembershipCopies memberships,
    IErasedPersonStore erased,
    IPersonalMessagePurge purge,
    ILearningUnitOfWork unitOfWork,
    ILogger<MembershipStartedV1Consumer> logger)
{
    /// <summary>The messages Learning stores that carry a person's name, purged for an erased person whose message is dropped.</summary>
    private static readonly IReadOnlyCollection<Type> PersonalMessages = [typeof(MembershipStartedV1), typeof(MembershipChangedV1)];

    public async Task HandleAsync(MembershipStartedV1 message, CancellationToken cancellationToken)
    {
        // Refused here, each by its name, so a poison message dead-letters with a reason that says which field. Name,
        // job function and role are not kept, so not checked either.
        Ensure.That(message).IsNotNull();
        Ensure.That(message.MembershipId, nameof(message.MembershipId)).IsNotEmpty();
        Ensure.That(message.PersonId, nameof(message.PersonId)).IsNotEmpty();
        Ensure.That(message.PharmacyId, nameof(message.PharmacyId)).IsNotEmpty();

        var person = PersonId.From(message.PersonId);
        await erased.HoldAsync(person, cancellationToken);
        if (await erased.IsErasedAsync(person, cancellationToken))
        {
            ErasedPersonDropped(logger, message.MembershipId);
            await purge.PurgeAsync(message.PersonId, PersonalMessages, cancellationToken);
            return;
        }

        if (await memberships.ExistsAsync(MembershipId.From(message.MembershipId), cancellationToken))
        {
            return;
        }

        memberships.Add(new MemberCopy { Id = message.MembershipId, PersonId = message.PersonId, PharmacyId = message.PharmacyId });
        await unitOfWork.CommitAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Membership {Membership} belongs to an erased person; its event is dropped.")]
    private static partial void ErasedPersonDropped(ILogger logger, Guid membership);
}
