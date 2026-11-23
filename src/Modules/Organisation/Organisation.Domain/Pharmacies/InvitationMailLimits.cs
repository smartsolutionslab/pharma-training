namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>
/// How often a pharmacy may request invitation mails: an invitation is renewed at most once per
/// <see cref="RenewCooldown"/>, and a pharmacy requests at most <see cref="DailyMailCap"/> mails per UTC day
/// (docs/adr/0004). The host's configuration sets them; <see cref="Default"/> is 10 minutes and 50 mails.
/// </summary>
public sealed record InvitationMailLimits(RenewCooldown RenewCooldown, DailyMailCap DailyMailCap)
{
    public const int DefaultRenewCooldownMinutes = 10;

    public const int DefaultMailsPerPharmacyPerDay = 50;

    public static InvitationMailLimits Default { get; } = new(
        RenewCooldown.From(TimeSpan.FromMinutes(DefaultRenewCooldownMinutes)),
        DailyMailCap.From(DefaultMailsPerPharmacyPerDay));
}
