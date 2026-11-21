using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.Samples;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// The shared row-version bump over Editorial's model, without a database: a change to a version's chapters alone
/// updates the version's row too, so its row version is checked; nothing else is touched.
/// </summary>
public sealed class RowVersionBumpTests : IDisposable
{
    private readonly EditorialDbContext context = new EditorialDbContextFactory().CreateDbContext([]);

    [Fact]
    public void The_course_version_carries_a_row_version_on_xmin()
    {
        var rowVersion = context.Model.FindEntityType(typeof(CourseVersion))!.FindProperty(RowVersion.PropertyName);

        rowVersion.ShouldNotBeNull();
        rowVersion.IsConcurrencyToken.ShouldBeTrue();
        rowVersion.GetColumnName().ShouldBe("xmin");
    }

    [Fact]
    public async Task A_removed_chapter_updates_its_version()
    {
        var version = DraftWithChapter(out var chapter);
        context.Attach(version);
        version.RemoveChapter(chapter).IsSuccess.ShouldBeTrue();

        await BumpAsync();

        context.Entry(version).State.ShouldBe(EntityState.Modified);
    }

    [Fact]
    public async Task An_added_chapter_updates_its_version()
    {
        var version = DraftWithChapter(out _);
        context.Attach(version);
        version.AddChapter(TextChapter(ChapterId.New())).IsSuccess.ShouldBeTrue();

        await BumpAsync();

        context.Entry(version).State.ShouldBe(EntityState.Modified);
    }

    [Fact]
    public async Task A_version_whose_chapters_did_not_change_is_left_as_it_is()
    {
        var version = DraftWithChapter(out _);
        context.Attach(version);

        await BumpAsync();

        context.Entry(version).State.ShouldBe(EntityState.Unchanged);
    }

    [Fact]
    public async Task An_entity_without_a_row_version_is_left_as_it_is()
    {
        var sample = Sample.Create(SampleId.New(), SampleName.From("Sample"));
        context.Attach(sample);

        await BumpAsync();

        context.Entry(sample).State.ShouldBe(EntityState.Unchanged);
    }

    public void Dispose() => context.Dispose();

    private async Task BumpAsync() =>
        await new RowVersionBump().SavingChangesAsync(new DbContextEventData(null!, null!, context), default, TestContext.Current.CancellationToken);
}
