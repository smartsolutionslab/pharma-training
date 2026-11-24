namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;

/// <summary>The refusal of the per-editor limits on the model's endpoints (docs/adr/0020).</summary>
public static class ModelLimitFailures
{
    public static ApiError Reached() => new ModelLimitReached();
}
