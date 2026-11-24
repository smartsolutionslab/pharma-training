using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationSenders;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// The people who requested invitation mails, with their daily tally; implemented in its Infrastructure project. What
/// a command adds or changes is saved when it commits the module's unit of work.
/// </summary>
public interface IInvitationSenderStore
{
    void Add(InvitationSender sender);

    /// <summary>The sender, for a command: what it changes is saved when the unit of work commits.</summary>
    Task<Option<InvitationSender>> LoadAsync(PersonId id, CancellationToken cancellationToken);
}
