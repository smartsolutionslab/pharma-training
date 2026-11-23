using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application.DTOs;

/// <summary>What a change of an assignment did, as the module's endpoints name it; travels by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ComplianceAssignmentChangeKind>))]
public enum ComplianceAssignmentChangeKind
{
    DueDateChanged,
    Cancelled,
}
