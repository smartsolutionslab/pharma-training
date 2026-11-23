using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;

/// <summary>A membership as its pharmacy's management sees it: an active one as a member, an invited one as an invitation.</summary>
public static class MembershipMappings
{
    /// <summary>An Active membership as a member; any other status is a bug.</summary>
    public static PharmacyMemberDto ToPharmacyMemberDto(this Membership membership)
    {
        Ensure.That(membership).IsNotNull();

        if (membership.Status != MembershipStatus.Active
            || membership.Person is not { } person
            || membership.Name is not { } name
            || membership.StartedAt is not { } since)
        {
            throw new UnreachableException($"Membership {membership.Id.Value} is {membership.Status}, not an active member.");
        }

        return new PharmacyMemberDto(
            membership.Id.Value,
            person.Value,
            name.Value,
            membership.JobFunction.ToDto(),
            membership.Role.ToDto(),
            since.Value);
    }

    /// <summary>An Invited membership as an invitation; any other status is a bug.</summary>
    public static PharmacyInvitationDto ToPharmacyInvitationDto(this Membership membership)
    {
        Ensure.That(membership).IsNotNull();

        if (membership.Status != MembershipStatus.Invited || membership.InvitationExpiresAt is not { } expiresAt)
        {
            throw new UnreachableException($"Membership {membership.Id.Value} is {membership.Status}, not an invitation.");
        }

        return new PharmacyInvitationDto(
            membership.Id.Value,
            membership.Email.Value,
            membership.JobFunction.ToDto(),
            membership.Role.ToDto(),
            expiresAt.Value);
    }
}
