namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>Which version of its invitation a membership holds, counted from 1 at the invite; each renewal is the next.</summary>
public sealed record InvitationVersion : IValueObject<int>
{
    private InvitationVersion(int value) => Value = value;

    public int Value { get; }

    public static InvitationVersion First => new(1);

    public static InvitationVersion From(int value)
    {
        Ensure.That(value).AtLeast(1);
        return new InvitationVersion(value);
    }

    public InvitationVersion Next() => new(Value + 1);
}
