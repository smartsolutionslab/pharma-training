using System.Globalization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;

/// <summary>How many months a completed course version stays valid, as Editorial sent it: at least one.</summary>
public sealed record ValidityMonths : IValueObject<int>
{
    private ValidityMonths(int value) => Value = value;

    public int Value { get; }

    public static ValidityMonths From(int value)
    {
        Ensure.That(value).AtLeast(1);
        return new ValidityMonths(value);
    }

    public override string ToString() => Value.ToString(CultureInfo.InvariantCulture);
}
