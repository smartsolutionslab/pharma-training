using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// Reports a pharmacy's invitation mail day beyond tomorrow, and an invitation's request time a renew cooldown or more
/// beyond now, which only a hand-edited row or a database clock that moved back (a failover or a restore) stores, since
/// every instance counts on the database's clock: the day is discarded and today's tally starts afresh, the request
/// time is not trusted and the invitation may be renewed (docs/adr/0004), and the warning lets an operator find the
/// row. It names ids, days and instants only, never an address.
/// </summary>
public static partial class InvitationMailClock
{
    /// <summary>Logs a warning when the pharmacy's stored mail day lies beyond tomorrow; call it before counting a mail.</summary>
    public static void WarnWhenAhead(ILogger logger, Pharmacy pharmacy, Timestamp now)
    {
        Ensure.That(logger).IsNotNull();
        Ensure.That(pharmacy).IsNotNull();
        Ensure.That(now).IsNotNull();

        var ahead = pharmacy.InvitationMailDayBeyondTomorrow(now);
        if (ahead.HasValue)
        {
            LogAhead(logger, pharmacy.Id.Value, ahead.Value.Value, UtcDay.Of(now).Value);
        }
    }

    /// <summary>
    /// Logs a warning when the invitation's stored request time lies a renew cooldown or more beyond now; call it before
    /// renewing, so it is logged even when another limit then refuses the renewal.
    /// </summary>
    public static void WarnWhenRequestedAhead(ILogger logger, Pharmacy pharmacy, MembershipId membership, Timestamp now, RenewCooldown cooldown)
    {
        Ensure.That(logger).IsNotNull();
        Ensure.That(pharmacy).IsNotNull();
        Ensure.That(membership).IsNotNull();
        Ensure.That(now).IsNotNull();
        Ensure.That(cooldown).IsNotNull();

        var ahead = pharmacy.InvitationRequestedBeyondCooldown(membership, now, cooldown);
        if (ahead.HasValue)
        {
            LogRequestedAhead(logger, membership.Value, pharmacy.Id.Value, ahead.Value.Value, now.Value);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pharmacy {Pharmacy} stored the invitation mail day {StoredDay:yyyy-MM-dd}, beyond tomorrow of today {Today:yyyy-MM-dd}; it is discarded and today's tally starts afresh. The row was edited by hand.")]
    private static partial void LogAhead(ILogger logger, Guid pharmacy, DateOnly storedDay, DateOnly today);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Invitation {Membership} of pharmacy {Pharmacy} stored the request time {RequestedAt:O}, a cooldown or more beyond now {Now:O}; it is not trusted and the invitation may be renewed. The row was edited by hand, or the database's clock moved back (a failover or a restore).")]
    private static partial void LogRequestedAhead(ILogger logger, Guid membership, Guid pharmacy, DateTimeOffset requestedAt, DateTimeOffset now);
}
