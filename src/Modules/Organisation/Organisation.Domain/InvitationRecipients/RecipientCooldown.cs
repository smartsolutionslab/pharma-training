namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;

/// <summary>How long after its latest invitation mail an address may be mailed again: more than nothing, at most a day.</summary>
public sealed record RecipientCooldown : IValueObject<TimeSpan>
{
    private RecipientCooldown(TimeSpan value) => Value = value;

    public TimeSpan Value { get; }

    public static RecipientCooldown From(TimeSpan value)
    {
        Ensure.That((decimal)value.Ticks).Satisfies(ticks => ticks > 0 && ticks <= TimeSpan.TicksPerDay, "A recipient cooldown is more than nothing and at most a day.");
        return new RecipientCooldown(value);
    }
}
