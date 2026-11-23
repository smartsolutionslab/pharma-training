namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>
/// One link issued for the open invitation's current version, kept as its token's hash only. Every link of that version
/// opens the invitation; a renewal, an accept or a revoke clears them all.
/// </summary>
public sealed class InvitationLink
{
    private InvitationLink(InvitationTokenHash hash) => Hash = hash;

    public InvitationTokenHash Hash { get; }

    internal static InvitationLink Of(InvitationTokenHash hash)
    {
        Ensure.That(hash).IsNotNull();
        return new InvitationLink(hash);
    }
}
