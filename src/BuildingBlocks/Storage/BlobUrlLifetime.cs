namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;

/// <summary>
/// How long a signed blob URL works: more than zero and at most <see cref="Maximum"/>, fifteen minutes. A URL is a bearer
/// credential for its blob; whoever holds it may use it until it expires, so it never lives long.
/// </summary>
public sealed record BlobUrlLifetime : IValueObject<TimeSpan>
{
    private BlobUrlLifetime(TimeSpan value) => Value = value;

    public static TimeSpan Maximum { get; } = TimeSpan.FromMinutes(15);

    public TimeSpan Value { get; }

    public static BlobUrlLifetime From(TimeSpan value)
    {
        Ensure.That((decimal)value.Ticks, nameof(value))
            .Satisfies(ticks => ticks > 0 && ticks <= Maximum.Ticks, $"must be more than zero and at most {Maximum.TotalMinutes} minutes; got {value}.");
        return new BlobUrlLifetime(value);
    }

    public override string ToString() => Value.ToString();
}
