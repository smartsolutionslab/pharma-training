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
}
