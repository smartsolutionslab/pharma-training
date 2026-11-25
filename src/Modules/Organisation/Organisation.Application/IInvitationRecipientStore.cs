using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// The addresses mailed an invitation, by derived id; implemented in its Infrastructure project. What a command changes
/// is saved when it commits the module's unit of work.
/// </summary>
public interface IInvitationRecipientStore
{
    /// <summary>
    /// The address's recipient, for a command that counts a mail on it (docs/adr/0004, #375). An address that has none
    /// gets a row first, <see cref="InvitationRecipient.NotYetMailed"/>, and the recipient is held until the unit of
    /// work commits or is discarded: two commands mailing one address at once, the very first two included, count one
    /// after the other on one row. It is held before the sender (<see cref="IInvitationSenderStore"/>), never after,
    /// so two commands never wait for each other's rows. A command that does not commit leaves no new row behind. A
    /// wait for the row that runs past the store's lock timeout ends as a lost race (<see cref="SaveDeadlocked"/>),
    /// having changed nothing, and the command commits nothing.
    /// </summary>
    Task<Result<InvitationRecipient, LostRace>> LoadForMailAsync(EmailAddress address, CancellationToken cancellationToken);
}
