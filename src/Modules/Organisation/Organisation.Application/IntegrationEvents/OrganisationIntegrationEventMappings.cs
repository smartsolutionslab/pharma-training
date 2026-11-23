using SmartSolutionsLab.PharmaTraining.Contracts.Organisation;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.IntegrationEvents;

/// <summary>How the module's domain events are announced to other modules; pure, no I/O.</summary>
public static class OrganisationIntegrationEventMappings
{
    public static PharmacyRegisteredV1 ToIntegrationEvent(this PharmacyRegistered registered)
    {
        Ensure.That(registered).IsNotNull();

        return new PharmacyRegisteredV1(registered.Pharmacy.Value, registered.Name.Value);
    }

    public static MembershipStartedV1 ToIntegrationEvent(this MembershipStarted started)
    {
        Ensure.That(started).IsNotNull();

        return new MembershipStartedV1(
            started.Membership.Value,
            started.Person.Value,
            started.Pharmacy.Value,
            started.Name.Value,
            started.JobFunction.ToV1(),
            started.Role.ToV1(),
            started.StartedAt.Value);
    }

    public static MembershipChangedV1 ToIntegrationEvent(this MembershipChanged changed)
    {
        Ensure.That(changed).IsNotNull();

        return new MembershipChangedV1(
            changed.Membership.Value,
            changed.Person.Value,
            changed.Pharmacy.Value,
            changed.Name.Value,
            changed.JobFunction.ToV1(),
            changed.Role.ToV1(),
            changed.Version.Value);
    }

    /// <summary>Organisation's own work item: ids only, so its envelope and dead letter hold no personal data.</summary>
    public static InvitationMailRequestedV1 ToIntegrationEvent(this MemberInvited invited)
    {
        Ensure.That(invited).IsNotNull();

        return new InvitationMailRequestedV1(invited.Membership.Value, invited.Pharmacy.Value);
    }

    public static MembershipEndedV1 ToIntegrationEvent(this MembershipEnded ended)
    {
        Ensure.That(ended).IsNotNull();

        return new MembershipEndedV1(ended.Membership.Value, ended.Person.Value, ended.Pharmacy.Value, ended.EndedAt.Value);
    }
}
