using System.Security.Cryptography;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>Pharmacies as the use cases find them in the store: registered, with members who accepted their invitation.</summary>
internal static class TestPharmacies
{
    public static readonly Timestamp RegisteredAt = Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));

    /// <summary>A pharmacy whose founder is its only active manager.</summary>
    public static Pharmacy Register(PersonId founder, string name = "Stadt-Apotheke") =>
        Pharmacy.Register(
            PharmacyId.New(),
            PharmacyName.From(name),
            MembershipId.New(),
            founder,
            DisplayName.From("Olaf Owner"),
            EmailAddress.From($"owner-{founder.Value:N}@example.org"),
            JobFunction.Pharmacist,
            RegisteredAt);

    /// <summary>The founder's membership.</summary>
    public static MembershipId FounderOf(Pharmacy pharmacy) => pharmacy.Memberships[0].Id;

    /// <summary>Invites the person and lets them accept, so they are an active member with the role given.</summary>
    public static MembershipId AddMember(Pharmacy pharmacy, PersonId person, MembershipRole role, JobFunction jobFunction = JobFunction.Pta, string name = "Anna Berger")
    {
        var token = Token();
        var email = EmailAddress.From($"member-{person.Value:N}@example.org");
        var membership = MembershipId.New();
        pharmacy.Invite(membership, email, jobFunction, role, RegisteredAt, InvitationMailLimits.Default).IsSuccess.ShouldBeTrue();
        pharmacy.IssueInvitationLink(membership, token, RegisteredAt).IsSuccess.ShouldBeTrue();
        pharmacy.AcceptInvitation(token, person, DisplayName.From(name), email, RegisteredAt).IsSuccess.ShouldBeTrue();
        return membership;
    }

    /// <summary>An invitation nobody has accepted yet, whose mail has not issued its link yet, made at the time given (by default when the pharmacy was registered), so it runs out 14 days later.</summary>
    public static MembershipId Invite(Pharmacy pharmacy, string email, Timestamp? at = null)
    {
        var membership = MembershipId.New();
        pharmacy.Invite(membership, EmailAddress.From(email), JobFunction.Pta, MembershipRole.Employee, at ?? RegisteredAt, InvitationMailLimits.Default).IsSuccess.ShouldBeTrue();
        return membership;
    }

    /// <summary>Puts the pharmacy in the store as saved: the events its setup raised were dispatched with that save.</summary>
    public static Pharmacy Stored(this Pharmacy pharmacy, InMemoryPharmacyStore store)
    {
        pharmacy.ClearDomainEvents();
        store.Stored.Add(pharmacy);
        return pharmacy;
    }

    public static Membership Membership(this Pharmacy pharmacy, MembershipId id) => pharmacy.Memberships.Single(membership => membership.Id == id);

    private static InvitationToken Token() => InvitationToken.From(RandomNumberGenerator.GetHexString(64));
}
