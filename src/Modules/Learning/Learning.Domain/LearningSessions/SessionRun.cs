namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Domain.LearningSessions;

/// <summary>Which of a learner's runs of one course version a session is, counting from 1; a new year's repetition is the next.</summary>
public sealed record SessionRun : IValueObject<int>
{
    private SessionRun(int value) => Value = value;

    public int Value { get; }

    public static SessionRun First => new(1);

    public static SessionRun From(int value)
    {
        Ensure.That(value).AtLeast(1);
        return new SessionRun(value);
    }

    public SessionRun Next() => From(Value + 1);

    public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
