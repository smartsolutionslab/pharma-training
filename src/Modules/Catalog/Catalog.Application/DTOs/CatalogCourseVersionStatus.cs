using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.DTOs;

/// <summary>
/// A course version's status in the catalog, derived when it is read; travels by name. Retired wins over superseded.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<CatalogCourseVersionStatus>))]
public enum CatalogCourseVersionStatus
{
    /// <summary>Not retired, and no newer version of its course is stored.</summary>
    Current,

    /// <summary>Not retired, and a newer version of its course is stored, retired or not.</summary>
    Superseded,

    /// <summary>Editorial retired it.</summary>
    Retired,
}
