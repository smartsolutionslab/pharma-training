using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.CreateCourse;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

public class CreateCourseTests
{
    private static readonly PersonId Editor = PersonId.From(Guid.CreateVersion7());

    [Fact]
    public async Task A_new_course_is_stored_as_its_first_draft_by_the_editor_once_committed_and_the_versions_id_answered()
    {
        var store = new InMemoryCourseVersionStore();

        var created = await new CreateCourseHandler(store, store).HandleAsync(new CreateCourse(CourseTitle.From("Hygiene 2027"), Editor), TestContext.Current.CancellationToken);

        var version = store.Stored.ShouldHaveSingleItem();
        created.Value.ShouldBe(version.Id.Value);
        created.Value.Version.ShouldBe(7);
        version.Course.Value.Version.ShouldBe(7);
        version.Number.ShouldBe(VersionNumber.First);
        version.Status.ShouldBe(CourseVersionStatus.Draft);
        version.Title.ShouldBe(CourseTitle.From("Hygiene 2027"));
        version.CreatedBy.ShouldBe(Editor);
        version.Blueprint.ShouldBe(TestBlueprint.Default);
        version.Chapters.ShouldBeEmpty();
        store.Commits.ShouldBe(1);
    }

    [Fact]
    public async Task Every_new_course_is_a_course_of_its_own()
    {
        var store = new InMemoryCourseVersionStore();
        var handler = new CreateCourseHandler(store, store);

        await handler.HandleAsync(new CreateCourse(CourseTitle.From("Hygiene 2027"), Editor), TestContext.Current.CancellationToken);
        await handler.HandleAsync(new CreateCourse(CourseTitle.From("Hygiene 2027"), Editor), TestContext.Current.CancellationToken);

        store.Stored.Select(version => version.Course).Distinct().Count().ShouldBe(2);
    }
}
