using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

/// <summary>What a member does in the pharmacy, as the module's endpoints name it; travels by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ComplianceJobFunction>))]
public enum ComplianceJobFunction
{
    Pharmacist,
    Pta,
    Pka,
    Trainee,
    Other,
}
