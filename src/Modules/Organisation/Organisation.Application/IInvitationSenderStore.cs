using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationSenders;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// The people who requested invitation mails, with their daily tally; implemented in its Infrastructure project. What
/// a command changes is saved when it commits the module's unit of work.
/// </summary>
public interface IInvitationSenderStore
{
    /// <summary>
    /// The sender, for a command that counts a mail on them (docs/adr/0004, #369). A sender who has none gets a row first,
    /// <see cref="InvitationSender.NotYetMailed"/> on <paramref name="now"/>'s day, and the sender is held until the
    /// unit of work commits or is discarded: two commands counting on one sender at once, the very first two included,
    /// count one after the other on one row. It is held after the recipient (<see cref="IInvitationRecipientStore"/>),
    /// never before. A command that does not commit leaves no new row behind. A wait for the row that runs past the
    /// store's lock timeout ends as a lost race (<see cref="SaveDeadlocked"/>), having changed nothing, and the command
    /// commits nothing (#375).
    /// </summary>
    Task<Result<InvitationSender, LostRace>> LoadForMailAsync(PersonId id, Timestamp now, CancellationToken cancellationToken);

    /// <summary>Removes the sender's tally when the unit of work commits; a sender who has none is no change.</summary>
    Task RemoveAsync(PersonId id, CancellationToken cancellationToken);
}
