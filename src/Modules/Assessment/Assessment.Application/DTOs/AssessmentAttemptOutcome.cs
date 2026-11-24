using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.DTOs;

[JsonConverter(typeof(JsonStringEnumConverter<AssessmentAttemptOutcome>))]
public enum AssessmentAttemptOutcome
{
    Passed,
    Failed,
}
