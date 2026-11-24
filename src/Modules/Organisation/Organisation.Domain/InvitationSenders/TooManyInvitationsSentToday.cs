using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.InvitationSenders;

/// <summary>Requesting an invitation mail beyond the sender's daily cap, across every pharmacy; they may again from <paramref name="RetryAt"/>, the next 00:00 UTC; refused at <paramref name="RefusedAt"/>, on the clock <paramref name="RetryAt"/> was computed on.</summary>
public sealed record TooManyInvitationsSentToday(PersonId Sender, Timestamp RetryAt, Timestamp RefusedAt)
    : DomainError("organisation.too-many-invitations-sent-today", "You have sent as many invitation mails today as you may; try again later.");
