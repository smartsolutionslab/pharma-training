namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>The secret in an invitation link. Created by the Application from a cryptographic random source.</summary>
public sealed record InvitationToken : IValueObject<string>
{
    public const int MinLength = 32;

    private InvitationToken(string value) => Value = value;

    public string Value { get; }

    public static InvitationToken From(string value)
    {
        Ensure.That(value).IsNotNullOrWhiteSpace().HasMinLength(MinLength);
        return new InvitationToken(value);
    }

    /// <summary>Never the secret itself, so no log or record print shows it; read <see cref="Value"/> for the link.</summary>
    public override string ToString() => "InvitationToken { *** }";
}
