using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// The module's database's current time; implemented in its Infrastructure project. Invitations take every instant
/// from it, not from each instance's <see cref="TimeProvider"/>: the mail limits, the request time and expiry an invite
/// or renewal stores, and the expiry check when a link is issued or the invitation accepted, so every instance counts
/// and checks on one clock (docs/adr/0004).
/// </summary>
public interface IDatabaseClock
{
    /// <summary>The database's current instant, in UTC.</summary>
    Task<Timestamp> NowAsync(CancellationToken cancellationToken);
}
