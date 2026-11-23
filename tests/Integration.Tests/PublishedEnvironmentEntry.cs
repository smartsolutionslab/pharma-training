namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// One entry of a container's <c>env</c> in a Container App's or job's Bicep: its name, and either the value as written
/// (a quoted literal or an expression) or the name of the app's secret it reads (<c>secretRef</c>), the other empty.
/// </summary>
public sealed record PublishedEnvironmentEntry(string Name, string Value, string SecretRef);
