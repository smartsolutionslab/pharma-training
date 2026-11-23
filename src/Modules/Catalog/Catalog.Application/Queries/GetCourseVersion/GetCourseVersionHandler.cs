using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.Queries.GetCourseVersion;

public sealed class GetCourseVersionHandler(ICourseVersionStore versions)
    : IQueryHandler<GetCourseVersion, Result<CatalogCourseVersionDto, ApiError>>
{
    public async Task<Result<CatalogCourseVersionDto, ApiError>> HandleAsync(GetCourseVersion query, CancellationToken cancellationToken)
    {
        Ensure.That(query).IsNotNull();

        if (query.Id == Guid.Empty)
        {
            return Failure(CourseVersionFailures.NotFound(query.Id));
        }

        var version = await versions.FindAsync(query.Id, cancellationToken);
        if (!version.HasValue)
        {
            return Failure(CourseVersionFailures.NotFound(query.Id));
        }

        var isRetired = await versions.IsRetiredAsync(query.Id, cancellationToken);
        var isSuperseded = await versions.HasNewerVersionAsync(version.Value.CourseId, version.Value.VersionNumber, cancellationToken);
        return Success(version.Value.ToCatalogCourseVersionDto(StatusOf(isRetired, isSuperseded)));
    }

    // Derived from what is stored on every read, as the list decides the current version: nothing stored changes.
    private static CatalogCourseVersionStatus StatusOf(bool isRetired, bool isSuperseded)
    {
        if (isRetired)
        {
            return CatalogCourseVersionStatus.Retired;
        }

        if (isSuperseded)
        {
            return CatalogCourseVersionStatus.Superseded;
        }

        return CatalogCourseVersionStatus.Current;
    }
}
