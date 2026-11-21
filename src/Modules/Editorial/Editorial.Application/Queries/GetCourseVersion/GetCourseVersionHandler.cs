using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.GetCourseVersion;

public sealed class GetCourseVersionHandler(ICourseVersionStore versions)
    : IQueryHandler<GetCourseVersion, Result<EditorialCourseVersionDto, ApiError>>
{
    public async Task<Result<EditorialCourseVersionDto, ApiError>> HandleAsync(GetCourseVersion query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        if (query.Id == Guid.Empty)
        {
            return Failure(CourseVersionFailures.NotFound(query.Id));
        }

        var version = await versions.FindAsync(CourseVersionId.From(query.Id), cancellationToken);
        if (!version.HasValue)
        {
            return Failure(CourseVersionFailures.NotFound(query.Id));
        }

        return Success(version.Value.ToDto());
    }
}
