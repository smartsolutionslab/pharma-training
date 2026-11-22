using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>A test blueprint as the module's endpoints answer it.</summary>
public static class TestBlueprintMappings
{
    // Red-first stub for #17: answers no value of the blueprint until the change lands.
    public static EditorialTestBlueprintDto ToDto(this TestBlueprint blueprint)
    {
        Ensure.That(blueprint).IsNotNull();
        return new EditorialTestBlueprintDto(0, 0, 0);
    }
}
