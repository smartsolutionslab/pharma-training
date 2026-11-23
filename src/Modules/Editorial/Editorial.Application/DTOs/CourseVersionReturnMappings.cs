using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>A return of a course version as the module's endpoints answer it.</summary>
public static class CourseVersionReturnMappings
{
    public static EditorialCourseVersionReturnDto ToDto(this CourseVersionReturn record)
    {
        Ensure.That(record).IsNotNull();

        return new EditorialCourseVersionReturnDto(record.Id.Value, record.ReturnedBy.Value, record.ReturnedAt.Value, record.Reason.Value);
    }
}
