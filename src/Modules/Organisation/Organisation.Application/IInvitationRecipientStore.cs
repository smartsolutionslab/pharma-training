using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// The addresses mailed an invitation, by derived id; implemented in its Infrastructure project. What a command adds
/// or changes is saved when it commits the module's unit of work.
/// </summary>
public interface IInvitationRecipientStore
{
    void Add(InvitationRecipient recipient);

    /// <summary>The recipient, for a command: what it changes is saved when the unit of work commits.</summary>
    Task<Option<InvitationRecipient>> LoadAsync(InvitationRecipientId id, CancellationToken cancellationToken);
}
