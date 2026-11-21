namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Llm;

/// <summary>
/// The model is needed and one of its settings is missing: the AppHost was run without the parameter that supplies it.
/// The host answers it with <c>503</c> problem details, code <c>model-not-configured</c>, not a <c>500</c>.
/// </summary>
public sealed class ModelNotConfiguredException : InvalidOperationException
{
    public ModelNotConfiguredException(string setting, string parameter)
        : base($"{setting} is not set. The AppHost supplies it from its parameter '{parameter}': set that with dotnet user-secrets (Parameters:{parameter}) or in the deployment's secret store.")
    {
        Parameter = parameter;
    }

    /// <summary>The AppHost parameter whose value is missing, such as <c>azure-openai-api-key</c>.</summary>
    public string Parameter { get; }
}
