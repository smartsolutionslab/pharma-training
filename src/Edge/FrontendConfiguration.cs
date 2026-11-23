namespace SmartSolutionsLab.PharmaTraining.Edge;

/// <summary>
/// What the web app needs to know before it signs in, read at runtime so one build serves every environment: the realm it
/// signs in with and its client there. A schema name no module uses, so the OpenAPI document and the generated client name it.
/// </summary>
public sealed record FrontendConfiguration(string OidcAuthority, string ClientId);
