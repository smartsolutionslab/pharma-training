namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>
/// The SHA-256 of an invitation token, the only form in which the token is stored.
/// Stub for #9's red tests (T1): <see cref="Of"/> and <see cref="From"/> are not implemented yet.
/// </summary>
public sealed record InvitationTokenHash : IValueObject<string>
{
    private InvitationTokenHash(string value) => Value = value;

    public string Value { get; }

    public static InvitationTokenHash From(string value) => new(value);

    public static InvitationTokenHash Of(InvitationToken token) => From(new string('0', 64));
}
