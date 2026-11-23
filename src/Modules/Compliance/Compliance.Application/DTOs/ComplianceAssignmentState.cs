using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

/// <summary>What an assignment shows on a given day, as the module's endpoints name it; travels by name. A cancelled one is never shown.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ComplianceAssignmentState>))]
public enum ComplianceAssignmentState
{
    Open,
    InProgress,
    Locked,
    Overdue,
    Completed,
    CompletedLate,
}
