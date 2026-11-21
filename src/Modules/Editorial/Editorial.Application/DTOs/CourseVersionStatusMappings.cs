using System.Diagnostics;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>A status under its own name in the endpoints and back; an undefined one is a bug.</summary>
public static class CourseVersionStatusMappings
{
    public static EditorialCourseVersionStatus ToDto(this CourseVersionStatus status) => status switch
    {
        CourseVersionStatus.Draft => EditorialCourseVersionStatus.Draft,
        CourseVersionStatus.InReview => EditorialCourseVersionStatus.InReview,
        CourseVersionStatus.Published => EditorialCourseVersionStatus.Published,
        CourseVersionStatus.Superseded => EditorialCourseVersionStatus.Superseded,
        CourseVersionStatus.Retired => EditorialCourseVersionStatus.Retired,
        _ => throw new UnreachableException($"Undefined course version status {status}."),
    };

    public static CourseVersionStatus ToDomain(this EditorialCourseVersionStatus status) => status switch
    {
        EditorialCourseVersionStatus.Draft => CourseVersionStatus.Draft,
        EditorialCourseVersionStatus.InReview => CourseVersionStatus.InReview,
        EditorialCourseVersionStatus.Published => CourseVersionStatus.Published,
        EditorialCourseVersionStatus.Superseded => CourseVersionStatus.Superseded,
        EditorialCourseVersionStatus.Retired => CourseVersionStatus.Retired,
        _ => throw new UnreachableException($"Undefined course version status {status}."),
    };
}
