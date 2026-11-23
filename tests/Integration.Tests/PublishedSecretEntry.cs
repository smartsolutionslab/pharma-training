namespace SmartSolutionsLab.PharmaTraining.Integration.Tests;

/// <summary>
/// One entry of a Container App's <c>configuration.secrets</c>: its name (what a <c>secretRef</c> names), the identity that
/// resolves it (<c>&lt;app&gt;-identity</c>, from <c>&lt;identity&gt;_outputs_id</c>; empty for a plain value), and the
/// Key Vault secret it reads (empty for a plain value).
/// </summary>
public sealed record PublishedSecretEntry(string Name, string Identity, string VaultSecret);
