using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Consumers;

/// <summary>
/// The invitation's version holds the most links whose mails may be out, so no further mail is sent for it. Thrown so the
/// mail request dead-letters and stays visible until a manager renews the invitation. Its message names only the
/// membership id and the code, never an address or a pharmacy's name, and it keeps no inner exception.
/// </summary>
public sealed class InvitationLinkLimitReachedException : InvalidOperationException
{
    public InvitationLinkLimitReachedException()
    {
    }

    public InvitationLinkLimitReachedException(string message)
        : base(message)
    {
    }

    public InvitationLinkLimitReachedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <param name="membership">The membership whose invitation holds the most links.</param>
    public InvitationLinkLimitReachedException(Guid membership)
        : base($"The invitation of membership {membership} holds {Pharmacy.MaxInvitationLinksPerVersion} links for its version already, so no mail is sent for it (organisation.invitation-link-limit-reached); renew the invitation.")
    {
    }
}
