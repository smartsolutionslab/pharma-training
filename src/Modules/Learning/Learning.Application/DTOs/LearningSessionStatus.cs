using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Learning.Application.DTOs;

/// <summary>Where a learning session stands, as the endpoints answer it; travels by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<LearningSessionStatus>))]
public enum LearningSessionStatus
{
    InProgress,
    ContentCompleted,
    Finished,
}
