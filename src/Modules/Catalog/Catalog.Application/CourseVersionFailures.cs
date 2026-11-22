namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application;

/// <summary>Everything the course versions' use cases refuse, each with its own code.</summary>
public static class CourseVersionFailures
{
    public static ApiError NotFound(Guid id) => new CourseVersionNotFound(id);
}
