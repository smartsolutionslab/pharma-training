using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// The module's database's current time; implemented in its Infrastructure project. The invitation mail limits take
/// their time from it, not from each instance's <see cref="TimeProvider"/>, so every instance counts on one clock
/// (docs/adr/0004).
/// </summary>
public interface IDatabaseClock
{
    /// <summary>The database's current instant, in UTC.</summary>
    Task<Timestamp> NowAsync(CancellationToken cancellationToken);
}
