using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.GetCourseVersion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>A course version is read back with its test blueprint, whatever its status.</summary>
public class TestBlueprintMappingsTests
{
    [Fact]
    public void The_default_blueprint_travels_as_ten_questions_80_percent_and_12_months()
    {
        TestBlueprint.Default.ToDto().ShouldBe(new EditorialTestBlueprintDto(10, 80, 12));
    }

    [Fact]
    public void A_set_blueprint_travels_with_its_values()
    {
        var blueprint = new TestBlueprint(QuestionCount.From(5), ScorePercent.From(70), RecurrenceInterval.From(24));

        blueprint.ToDto().ShouldBe(new EditorialTestBlueprintDto(5, 70, 24));
    }

    [Fact]
    public async Task A_new_version_is_read_with_the_default_blueprint()
    {
        var store = new InMemoryCourseVersionStore();
        var version = Draft();
        store.Stored.Add(version);

        var read = await new GetCourseVersionHandler(store).HandleAsync(new GetCourseVersion(version.Id.Value), TestContext.Current.CancellationToken);

        read.Value.Blueprint.ShouldBe(new EditorialTestBlueprintDto(10, 80, 12));
    }

    [Fact]
    public async Task A_published_version_is_read_with_its_blueprint()
    {
        var store = new InMemoryCourseVersionStore();
        var version = Published();
        store.Stored.Add(version);

        var read = await new GetCourseVersionHandler(store).HandleAsync(new GetCourseVersion(version.Id.Value), TestContext.Current.CancellationToken);

        read.Value.Blueprint.ShouldBe(new EditorialTestBlueprintDto(1, 80, 12));
    }
}
