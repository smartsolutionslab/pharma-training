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
    /// count one after the other on one row. A command that does not commit leaves no new row behind.
    /// </summary>
    Task<InvitationSender> LoadForMailAsync(PersonId id, Timestamp now, CancellationToken cancellationToken);
}
