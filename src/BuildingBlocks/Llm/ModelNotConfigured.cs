using System.Net;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Llm;

/// <summary>
/// A request needed the model while its endpoint or key is not set (<see cref="ModelNotConfiguredException"/>): the
/// host's generic <c>503</c>; everything else keeps working.
/// </summary>
public sealed record ModelNotConfigured()
    : ApiError("model-not-configured", "The AI model is not configured.", HttpStatusCode.ServiceUnavailable);
