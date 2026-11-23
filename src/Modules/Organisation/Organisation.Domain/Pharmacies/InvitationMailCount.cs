namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>How many invitation mails a pharmacy has requested on one UTC day.</summary>
public sealed record InvitationMailCount : IValueObject<int>
{
    private InvitationMailCount(int value) => Value = value;

    public int Value { get; }

    public static InvitationMailCount Zero => new(0);

    public static InvitationMailCount From(int value)
    {
        Ensure.That(value).AtLeast(0);
        return new InvitationMailCount(value);
    }

    public InvitationMailCount Next() => new(Value + 1);
}
