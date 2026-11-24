namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>How many invitation mails a pharmacy or a sender may request on one UTC day: from 1 to 10000.</summary>
public sealed record DailyMailCap : IValueObject<int>
{
    public const int Maximum = 10000;

    private DailyMailCap(int value) => Value = value;

    public int Value { get; }

    public static DailyMailCap From(int value)
    {
        Ensure.That(value).InRange(1, Maximum);
        return new DailyMailCap(value);
    }
}
