using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;

/// <summary>A member's role in the pharmacy, as the module's endpoints name it; travels by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OrganisationMembershipRole>))]
public enum OrganisationMembershipRole
{
    Manager,
    Employee,
}
