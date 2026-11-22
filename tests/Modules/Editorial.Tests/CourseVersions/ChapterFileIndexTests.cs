using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// One file, one chapter of a version, backed twice: the aggregate refuses it, and the chapters' table has a unique
/// index on the version and the file, so a write that slipped past the aggregate is refused by the database too.
/// </summary>
public sealed class ChapterFileIndexTests : IDisposable
{
    private readonly EditorialDbContext context = new EditorialDbContextFactory().CreateDbContext([]);

    [Fact]
    public void A_version_s_chapters_have_a_unique_index_on_their_file()
    {
        var chapter = context.Model.FindEntityType(typeof(Chapter));
        chapter.ShouldNotBeNull();

        var index = chapter.GetIndexes().SingleOrDefault(index =>
            index.Properties.Select(property => property.Name).SequenceEqual(["CourseVersionId", nameof(Chapter.File)]));

        index.ShouldNotBeNull("one file belongs to one chapter of a version, and the database backs the aggregate's check");
        index.IsUnique.ShouldBeTrue();
    }

    public void Dispose() => context.Dispose();
}
