using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// Editorial's model, without a database: a course's version numbers are backed twice, by the next version's handler
/// and by a unique index, which also serves the lookups by course alone.
/// </summary>
public sealed class CourseVersionModelTests : IDisposable
{
    private readonly EditorialDbContext context = new EditorialDbContextFactory().CreateDbContext([]);

    [Fact]
    public void A_course_version_number_is_unique_within_its_course()
    {
        var version = context.Model.FindEntityType(typeof(CourseVersion));
        version.ShouldNotBeNull();

        var index = version.GetIndexes().SingleOrDefault(candidate =>
            candidate.Properties.Select(property => property.Name).SequenceEqual([nameof(CourseVersion.Course), nameof(CourseVersion.Number)]));

        index.ShouldNotBeNull("no index on (Course, Number)");
        index.IsUnique.ShouldBeTrue();
        index.GetDatabaseName().ShouldBe("ix_course_versions_course_id_number");
    }

    [Fact]
    public void No_index_covers_the_course_alone()
    {
        var version = context.Model.FindEntityType(typeof(CourseVersion));
        version.ShouldNotBeNull();

        version.GetIndexes()
            .Where(candidate => candidate.Properties.Select(property => property.Name).SequenceEqual([nameof(CourseVersion.Course)]))
            .Select(candidate => candidate.GetDatabaseName())
            .ShouldBeEmpty();
    }

    /// <summary>Approvals recorded before #205 have no name, so the column is nullable; its limit is the value object's.</summary>
    [Fact]
    public void The_reviewers_name_is_stored_in_a_nullable_column_of_its_own_of_at_most_100_characters()
    {
        var question = context.Model.FindEntityType(typeof(Question));
        question.ShouldNotBeNull();

        var name = question.FindProperty(nameof(Question.ReviewedByName));

        name.ShouldNotBeNull("Question.ReviewedByName is not mapped");
        name.GetColumnName().ShouldBe("reviewed_by_name");
        name.IsNullable.ShouldBeTrue();
        name.GetMaxLength().ShouldBe(100);
    }

    /// <summary>
    /// A PDF chapter's extraction (#157) is two nullable columns of the chapters' table: the outcome by name, and the
    /// text unbounded like a text chapter's. Rows recorded before #157 hold null in both.
    /// </summary>
    [Fact]
    public void A_pdf_chapters_extraction_is_stored_in_two_nullable_columns_of_its_own()
    {
        var chapter = context.Model.FindEntityType(typeof(Chapter));
        chapter.ShouldNotBeNull();

        var extraction = chapter.FindProperty(nameof(Chapter.Extraction));
        var text = chapter.FindProperty(nameof(Chapter.ExtractedText));

        extraction.ShouldNotBeNull("Chapter.Extraction is not mapped");
        extraction.GetColumnName().ShouldBe("pdf_text_extraction");
        extraction.IsNullable.ShouldBeTrue();
        extraction.GetProviderClrType().ShouldBe(typeof(string), "the outcome is stored by name");
        extraction.GetMaxLength().ShouldBe(20);
        text.ShouldNotBeNull("Chapter.ExtractedText is not mapped");
        text.GetColumnName().ShouldBe("pdf_text");
        text.IsNullable.ShouldBeTrue();
        text.GetColumnType().ShouldBe("text");
    }

    /// <summary>The runner fails on a model without its migration: the columns come with <c>AddChapterPdfText</c>.</summary>
    [Fact]
    public void The_model_has_no_change_its_migrations_lack()
    {
        context.Database.GetMigrations().ShouldContain(migration => migration.EndsWith("_AddChapterPdfText", StringComparison.Ordinal));
        context.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    public void Dispose() => context.Dispose();
}
