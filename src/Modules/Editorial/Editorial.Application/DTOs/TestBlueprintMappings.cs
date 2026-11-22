using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>A test blueprint as the module's endpoints answer it.</summary>
public static class TestBlueprintMappings
{
    public static EditorialTestBlueprintDto ToDto(this TestBlueprint blueprint)
    {
        Ensure.That(blueprint).IsNotNull();
        return new EditorialTestBlueprintDto(blueprint.Questions.Value, blueprint.PassingScore.Value, blueprint.Validity.Value);
    }
}
