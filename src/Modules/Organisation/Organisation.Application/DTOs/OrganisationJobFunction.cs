using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;

/// <summary>What a member does in the pharmacy, as the module's endpoints name it; travels by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<OrganisationJobFunction>))]
public enum OrganisationJobFunction
{
    Pharmacist,
    Pta,
    Pka,
    Trainee,
    Other,
}
