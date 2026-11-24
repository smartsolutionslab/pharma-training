namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>
/// One link issued for the open invitation's current version, kept as its token's hash only: a value, equal to any link
/// with the same hash. Every link of that version opens the invitation; a renewal, an accept or a revoke clears them all.
/// Stored as a row of <c>invitation_links</c> owned by its membership, not converted to its hash like other value
/// objects (src/BuildingBlocks/Persistence, <c>ValueObjectConventions</c>).
/// </summary>
public sealed record InvitationLink : IValueObject<InvitationTokenHash>
{
    private InvitationLink(InvitationTokenHash hash) => Hash = hash;

    /// <summary>The hash of the link's token, its only value.</summary>
    public InvitationTokenHash Hash { get; }

    InvitationTokenHash IValueObject<InvitationTokenHash>.Value => Hash;

    public static InvitationLink From(InvitationTokenHash value)
    {
        Ensure.That(value).IsNotNull();
        return new InvitationLink(value);
    }
}
