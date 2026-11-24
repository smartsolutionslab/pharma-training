namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>How long after its latest mail request an invitation may be renewed again: more than nothing, at most a day.</summary>
public sealed record RenewCooldown : IValueObject<TimeSpan>
{
    private RenewCooldown(TimeSpan value) => Value = value;

    public TimeSpan Value { get; }

    public static RenewCooldown From(TimeSpan value)
    {
        Ensure.That((decimal)value.Ticks).Satisfies(ticks => ticks > 0 && ticks <= TimeSpan.TicksPerDay, $"a renew cooldown is more than nothing and at most a day; got {value}.");
        return new RenewCooldown(value);
    }
}
