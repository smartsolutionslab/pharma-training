using System.Globalization;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Api;

/// <summary>
/// The module's limits from the host's configuration, read once at start-up (docs/adr/0004, ADR 0007): an invitation
/// is renewed at most every <c>Organisation:InvitationLimits:RenewCooldownMinutes</c> (1 to 1440, 10 without it) and
/// a pharmacy requests at most <c>Organisation:InvitationLimits:MailsPerPharmacyPerDay</c> invitation mails a UTC day
/// (1 to 10000, 50 without it). A value that is no whole number in range stops the host, naming the key.
/// </summary>
public static class OrganisationLimits
{
    public const string RenewCooldownMinutesKey = "Organisation:InvitationLimits:RenewCooldownMinutes";

    public const string MailsPerPharmacyPerDayKey = "Organisation:InvitationLimits:MailsPerPharmacyPerDay";

    private const int MostCooldownMinutes = 24 * 60;

    public static InvitationMailLimits InvitationMailsFrom(IConfiguration configuration)
    {
        Ensure.That(configuration).IsNotNull();

        var cooldown = WholeNumber(configuration, RenewCooldownMinutesKey, InvitationMailLimits.DefaultRenewCooldownMinutes, MostCooldownMinutes);
        var cap = WholeNumber(configuration, MailsPerPharmacyPerDayKey, InvitationMailLimits.DefaultMailsPerPharmacyPerDay, DailyMailCap.Maximum);
        return new InvitationMailLimits(RenewCooldown.From(TimeSpan.FromMinutes(cooldown)), DailyMailCap.From(cap));
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
            throw new InvalidOperationException($"The setting {key} is '{value}'; it accepts a whole number from 1 to {most}, or nothing for {fallback}.");
        }

        return number;
    }
}
