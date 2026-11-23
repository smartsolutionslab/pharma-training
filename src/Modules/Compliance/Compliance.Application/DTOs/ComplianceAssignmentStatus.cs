using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

/// <summary>An assignment's stored lifecycle as the module's endpoints name it; travels by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ComplianceAssignmentStatus>))]
public enum ComplianceAssignmentStatus
{
    Open,
    InProgress,
    Completed,
    Cancelled,
}
