using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Compliance.Infrastructure;

/// <summary>
/// The course versions and their retirements. The module's unit of work saves what is added; nothing is ever updated,
/// so two deliveries of one event race only on its primary key.
/// </summary>
public sealed class CourseVersionStore(ComplianceDbContext context) : ICourseVersionStore
{
    public Task<bool> ExistsAsync(CourseVersionId version, CancellationToken cancellationToken) =>
        context.Set<CourseVersion>().AnyAsync(stored => stored.Id == version, cancellationToken);

    public void Add(CourseVersion version) => context.Add(version);

    public Task<bool> IsRetiredAsync(CourseVersionId version, CancellationToken cancellationToken) =>
        context.Set<CourseVersionRetirement>().AnyAsync(retirement => retirement.Id == version, cancellationToken);

    public void Add(CourseVersionRetirement retirement) => context.Add(retirement);

    public async Task<Option<PublishedCourse>> FindCourseAsync(CourseId course, CancellationToken cancellationToken)
    {
        var versions = await context.Set<CourseVersion>().AsNoTracking().Where(version => version.Course == course).ToListAsync(cancellationToken);
        var retirements = await context.Set<CourseVersionRetirement>().AsNoTracking().Where(retirement => retirement.Course == course).ToListAsync(cancellationToken);
        return PublishedCourse.Of(course, versions, retirements);
    }
}
