using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.DTOs;

[JsonConverter(typeof(JsonStringEnumConverter<AssessmentExaminationStatus>))]
public enum AssessmentExaminationStatus
{
    Locked,
    Unlocked,
    InAttempt,
    Passed,
    Exhausted,
}
