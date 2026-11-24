using System.Globalization;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.RateLimits;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationRecipients;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Api;

/// <summary>
/// The module's limits from the host's configuration, read once at start-up (docs/adr/0004, ADR 0007): an invitation
/// is renewed at most every <c>Organisation:InvitationLimits:RenewCooldownMinutes</c> (1 to 1440, 10 without it) and
/// a pharmacy requests at most <c>Organisation:InvitationLimits:MailsPerPharmacyPerDay</c> invitation mails a UTC day
/// (1 to 10000, 50 without it). Across pharmacies, an address is mailed an invitation at most every
/// <c>Organisation:InvitationLimits:RecipientCooldownMinutes</c> (1 to 1440, 10 without it) and a signed-in user
/// requests at most <c>Organisation:InvitationLimits:MailsPerSenderPerDay</c> invitation mails a UTC day (1 to 10000,
/// 100 without it). A user accepts invitations at most <c>Organisation:AcceptLimit:PerMinute</c> times a
/// minute (10 without it) and <c>Organisation:AcceptLimit:PerDay</c> times a day (100 without it), counted in the
/// service's memory (docs/adr/0020). A value that is no whole number in range stops the host, naming the key.
/// </summary>
public static class OrganisationLimits
{
    /// <summary>The name of the per-user limit on accepting invitations, for <c>RequireUserRateLimit</c>.</summary>
    public const string AcceptInvitation = "organisation.accept-invitation";

    public const string AcceptPerMinuteKey = "Organisation:AcceptLimit:PerMinute";

    public const string AcceptPerDayKey = "Organisation:AcceptLimit:PerDay";

    public const int DefaultAcceptsPerMinute = 10;

    public const int DefaultAcceptsPerDay = 100;

    public const string RenewCooldownMinutesKey = "Organisation:InvitationLimits:RenewCooldownMinutes";

    public const string MailsPerPharmacyPerDayKey = "Organisation:InvitationLimits:MailsPerPharmacyPerDay";

    public const string RecipientCooldownMinutesKey = "Organisation:InvitationLimits:RecipientCooldownMinutes";

    public const string MailsPerSenderPerDayKey = "Organisation:InvitationLimits:MailsPerSenderPerDay";

    private const int MostCooldownMinutes = 24 * 60;

    public static InvitationMailLimits InvitationMailsFrom(IConfiguration configuration)
    {
        Ensure.That(configuration).IsNotNull();

        var cooldown = WholeNumber(configuration, RenewCooldownMinutesKey, InvitationMailLimits.DefaultRenewCooldownMinutes, MostCooldownMinutes);
        var cap = WholeNumber(configuration, MailsPerPharmacyPerDayKey, InvitationMailLimits.DefaultMailsPerPharmacyPerDay, DailyMailCap.Maximum);
        return new InvitationMailLimits(RenewCooldown.From(TimeSpan.FromMinutes(cooldown)), DailyMailCap.From(cap));
    }

    /// <summary>The limits across pharmacies: how often an address is mailed and how many mails a sender requests a UTC day.</summary>
    public static RecipientAndSenderMailLimits RecipientAndSenderMailsFrom(IConfiguration configuration)
    {
        Ensure.That(configuration).IsNotNull();

        var cooldown = WholeNumber(configuration, RecipientCooldownMinutesKey, RecipientAndSenderMailLimits.DefaultRecipientCooldownMinutes, MostCooldownMinutes);
        var cap = WholeNumber(configuration, MailsPerSenderPerDayKey, RecipientAndSenderMailLimits.DefaultMailsPerSenderPerDay, DailyMailCap.Maximum);
        return new RecipientAndSenderMailLimits(RecipientCooldown.From(TimeSpan.FromMinutes(cooldown)), DailyMailCap.From(cap));
    }

    /// <summary>The per-user limit on accepting invitations: so many a minute and so many a day.</summary>
    public static UserRateLimit AcceptFrom(IConfiguration configuration)
    {
        Ensure.That(configuration).IsNotNull();

        var perMinute = WholeNumber(configuration, AcceptPerMinuteKey, DefaultAcceptsPerMinute, int.MaxValue);
        var perDay = WholeNumber(configuration, AcceptPerDayKey, DefaultAcceptsPerDay, int.MaxValue);
        return UserRateLimit.Of(
            AcceptInvitation,
            PharmacyFailures.AcceptLimitReached(),
            UserRateWindow.Of(perMinute, TimeSpan.FromMinutes(1)),
            UserRateWindow.Of(perDay, TimeSpan.FromDays(1)));
    }

    private static int WholeNumber(IConfiguration configuration, string key, int fallback, int most)
    {
        var value = configuration[key];
        if (string.IsNullOrWhiteSpace(value))
        {
            return fallback;
        }

        if (!int.TryParse(value, NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite, CultureInfo.InvariantCulture, out var number) || number < 1 || number > most)
        {
            var range = most == int.MaxValue ? "from 1" : $"from 1 to {most}";
            throw new InvalidOperationException($"The setting {key} is '{value}'; it accepts a whole number {range}, or nothing for {fallback}.");
        }

        return number;
    }
}
