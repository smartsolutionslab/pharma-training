using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Persistence;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.Samples;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.QuestionDrafts;

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

    [Fact]
    public void Questions_and_their_options_are_stored_in_tables_of_their_own_beneath_the_version()
    {
        var question = context.Model.FindEntityType(typeof(Question));
        var option = context.Model.FindEntityType(typeof(AnswerOption));

        question.ShouldNotBeNull();
        option.ShouldNotBeNull();
        question.GetTableName().ShouldBe("course_version_questions");
        option.GetTableName().ShouldBe("course_version_answer_options");
        question.GetForeignKeys().ShouldContain(key => key.PrincipalEntityType.ClrType == typeof(CourseVersion) && key.DeleteBehavior == DeleteBehavior.Cascade);
        question.GetForeignKeys().ShouldContain(key =>
            key.PrincipalEntityType.ClrType == typeof(Chapter)
            && key.Properties.Single().GetColumnName() == "chapter_id"
            && key.DeleteBehavior == DeleteBehavior.Restrict);
        option.GetForeignKeys().ShouldContain(key => key.PrincipalEntityType.ClrType == typeof(Question) && key.DeleteBehavior == DeleteBehavior.Cascade);
    }

    [Fact]
    public async Task An_added_question_updates_its_version()
    {
        var version = DraftWithChapter(out var chapter);
        context.Attach(version);
        version.AddQuestion(QuestionId.New(), SingleChoice(chapter), AnswerOptionId.New).IsSuccess.ShouldBeTrue();

        await BumpAsync();

        context.Entry(version).State.ShouldBe(EntityState.Modified);
    }

    [Fact]
    public async Task An_edited_question_whose_options_were_replaced_updates_its_version()
    {
        var version = DraftWithApprovedQuestion(out var chapter, out var question);
        context.Attach(version);
        version.EditQuestion(question, MultipleChoice(chapter), Author, AnswerOptionId.New).IsSuccess.ShouldBeTrue();

        await BumpAsync();

        context.Entry(version).State.ShouldBe(EntityState.Modified);
    }

    [Fact]
    public async Task An_approved_question_updates_its_version()
    {
        var version = DraftWithQuestion(out _, out var question);
        context.Attach(version);
        version.ApproveQuestion(question, Reviewer, ReviewersName, Now).IsSuccess.ShouldBeTrue();

        await BumpAsync();

        context.Entry(version).State.ShouldBe(EntityState.Modified);
    }

    [Fact]
    public async Task A_removed_question_updates_its_version()
    {
        var version = DraftWithQuestion(out _, out var question);
        context.Attach(version);
        version.RemoveQuestion(question).IsSuccess.ShouldBeTrue();

        await BumpAsync();

        context.Entry(version).State.ShouldBe(EntityState.Modified);
    }

    public void Dispose() => context.Dispose();

    private async Task BumpAsync() =>
        await new RowVersionBump().SavingChangesAsync(new DbContextEventData(null!, null!, context), default, TestContext.Current.CancellationToken);
}
