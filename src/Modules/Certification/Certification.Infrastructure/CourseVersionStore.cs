using SmartSolutionsLab.PharmaTraining.Modules.Certification.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Courses;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Infrastructure;

/// <summary>
/// The course versions. The module's unit of work saves what is added; nothing is ever updated, so two deliveries of
/// one event race only on its primary key.
/// </summary>
public sealed class CourseVersionStore(CertificationDbContext context) : ICourseVersionStore
{
    public Task<bool> ExistsAsync(CourseVersionId version, CancellationToken cancellationToken) =>
        context.Set<CourseVersion>().AnyAsync(stored => stored.Id == version, cancellationToken);

    public void Add(CourseVersion version) => context.Add(version);

    public async Task<Option<CourseVersion>> FindAsync(CourseVersionId version, CancellationToken cancellationToken) =>
        await context.Set<CourseVersion>().AsNoTracking().SingleOrDefaultAsync(stored => stored.Id == version, cancellationToken) is { } found
            ? Option<CourseVersion>.Some(found)
            : Option<CourseVersion>.None;
}
