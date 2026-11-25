namespace SmartSolutionsLab.PharmaTraining.Edge;

/// <summary>
/// What the web app needs to know before it signs in, read at runtime so one build serves every environment: the realm it
/// signs in with and its client there, and the deployment the edge was published for (staging, production; another name in
/// run mode), which the demo seeder checks before it sends a credential to staging. A schema name no module uses, so the
/// OpenAPI document and the generated client name it.
/// </summary>
public sealed record FrontendConfiguration(string OidcAuthority, string ClientId, string Environment);
