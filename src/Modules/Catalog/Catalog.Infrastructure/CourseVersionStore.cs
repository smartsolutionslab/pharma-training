using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Infrastructure;

/// <summary>
/// The course versions, their chapters and their retirements. The module's unit of work saves what is added; nothing
/// is ever updated, so two deliveries of one version race only on its primary key.
/// </summary>
public sealed class CourseVersionStore(CatalogDbContext context) : ICourseVersionStore
{
    public Task<bool> ExistsAsync(Guid courseVersionId, CancellationToken cancellationToken) =>
        context.Set<CourseVersion>().AnyAsync(version => version.Id == courseVersionId, cancellationToken);

    public void Add(CourseVersion version) => context.Add(version);

    public Task<bool> IsRetiredAsync(Guid courseVersionId, CancellationToken cancellationToken) =>
        context.Set<CourseVersionRetirement>().AnyAsync(retirement => retirement.CourseVersionId == courseVersionId, cancellationToken);

    public void Add(CourseVersionRetirement retirement) => context.Add(retirement);

    public async Task<Option<CourseVersion>> FindAsync(Guid courseVersionId, CancellationToken cancellationToken) =>
        await context.Set<CourseVersion>()
            .AsNoTracking()
            .Include(version => version.Chapters.OrderBy(chapter => chapter.Position).ThenBy(chapter => chapter.ChapterId))
            .SingleOrDefaultAsync(version => version.Id == courseVersionId, cancellationToken) is { } version
            ? Option<CourseVersion>.Some(version)
            : Option<CourseVersion>.None;

    public async Task<PagedResult<CourseVersion>> ListCurrentAsync(Page page, PageSize pageSize, CancellationToken cancellationToken)
    {
        Ensure.That(page).IsNotNull();
        Ensure.That(pageSize).IsNotNull();

        var versions = context.Set<CourseVersion>().AsNoTracking();
        var retirements = context.Set<CourseVersionRetirement>();
        // A course's current version is its highest, and the course is listed only while that one is not retired.
        var current = versions
            .Where(version => !retirements.Any(retirement => retirement.CourseVersionId == version.Id))
            .Where(version => !versions.Any(other => other.CourseId == version.CourseId && other.VersionNumber > version.VersionNumber));
        var total = await current.CountAsync(cancellationToken);
        var items = await current
            .OrderBy(version => version.Title)
            .ThenBy(version => version.CourseId)
            .Skip(page.Skip(pageSize))
            .Take(pageSize.Value)
            .ToListAsync(cancellationToken);
        return new PagedResult<CourseVersion>(items, total, page.Value, pageSize.Value);
    }
}
