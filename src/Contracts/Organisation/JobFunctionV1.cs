using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Contracts.Organisation;

/// <summary>What a member does in the pharmacy; travels by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<JobFunctionV1>))]
public enum JobFunctionV1
{
    Pharmacist,
    Pta,
    Pka,
    Trainee,
    Other,
}
