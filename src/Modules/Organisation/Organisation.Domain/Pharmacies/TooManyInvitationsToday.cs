namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

/// <summary>Requesting an invitation mail beyond the pharmacy's daily cap; it may again from <paramref name="RetryAt"/>, the next 00:00 UTC.</summary>
public sealed record TooManyInvitationsToday(PharmacyId Pharmacy, Timestamp RetryAt)
    : DomainError("organisation.too-many-invitations-today", "The pharmacy has sent as many invitation mails today as it may; try again later.");
