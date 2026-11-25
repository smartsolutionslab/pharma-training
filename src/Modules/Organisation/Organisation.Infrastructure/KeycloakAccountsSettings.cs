namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure;

/// <summary>
/// How Organisation reaches Keycloak's admin API, bound from <c>Keycloak:Accounts</c>: Keycloak's internal address, the
/// realm, and the confidential client <c>pharma-training-erasure</c> with its secret, which the AppHost passes.
/// </summary>
public sealed class KeycloakAccountsSettings
{
    public const string Section = "Keycloak:Accounts";

    /// <summary>Keycloak's address, an absolute http(s) URI.</summary>
    public string Address { get; set; } = string.Empty;

    public string Realm { get; set; } = string.Empty;

    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;
}
