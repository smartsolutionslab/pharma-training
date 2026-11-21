using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>Where a course version is in its life, as the module's endpoints name it; travels by name.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EditorialCourseVersionStatus>))]
public enum EditorialCourseVersionStatus
{
    Draft,
    InReview,
    Published,
    Superseded,
    Retired,
}
