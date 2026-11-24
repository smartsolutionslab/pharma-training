using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// Reports a pharmacy's invitation mail day beyond tomorrow, which only a hand-edited row stores, since every instance
/// counts on the database's clock: it is discarded and today's tally starts afresh (docs/adr/0004), and the warning
/// lets an operator find the row. It names ids and days only.
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Pharmacy {Pharmacy} stored the invitation mail day {StoredDay:yyyy-MM-dd}, beyond tomorrow of today {Today:yyyy-MM-dd}; it is discarded and today's tally starts afresh. The row was edited by hand.")]
    private static partial void LogAhead(ILogger logger, Guid pharmacy, DateOnly storedDay, DateOnly today);
}
