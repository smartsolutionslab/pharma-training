namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>
/// The body that replaces a draft's test blueprint, all three values required. Each is nullable so that a value the
/// body lacks is refused rather than read as 0, which would be a valid passing score.
/// </summary>
public sealed record EditorialBlueprintRequest(int? Questions, int? PassingScorePercent, int? ValidityMonths);
