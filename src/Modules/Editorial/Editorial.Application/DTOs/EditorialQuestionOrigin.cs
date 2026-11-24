using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>How a question came into its version, as the module's endpoints name it (#245); travels by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EditorialQuestionOrigin>))]
public enum EditorialQuestionOrigin
{
    AiDraft,
    Copied,
    HandWritten,
}
