using Microsoft.Extensions.Options;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure;

/// <summary>
/// Refuses settings Organisation could not reach Keycloak's admin API with: an address that is not an absolute http(s)
/// URI, or a blank realm, client id or secret. Each failure names its key, never its value, so the secret stays out of the
/// start-up error and its log.
/// </summary>
internal sealed class KeycloakAccountsSettingsValidation : IValidateOptions<KeycloakAccountsSettings>
{
    public ValidateOptionsResult Validate(string? name, KeycloakAccountsSettings options)
    {
        Ensure.That(options).IsNotNull();

        List<string> failures = [];

        // A key present with no value binds null, whatever the property's annotation says.
        if (!Uri.TryCreate(options.Address?.Trim(), UriKind.Absolute, out var address) || (address.Scheme != Uri.UriSchemeHttp && address.Scheme != Uri.UriSchemeHttps))
        {
            failures.Add(Failure(nameof(KeycloakAccountsSettings.Address), "is missing or not an absolute http(s) address"));
        }

        if (string.IsNullOrWhiteSpace(options.Realm))
        {
            failures.Add(Failure(nameof(KeycloakAccountsSettings.Realm), "is missing"));
        }

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            failures.Add(Failure(nameof(KeycloakAccountsSettings.ClientId), "is missing"));
        }

        if (string.IsNullOrWhiteSpace(options.ClientSecret))
        {
            failures.Add(Failure(nameof(KeycloakAccountsSettings.ClientSecret), "is missing (the AppHost's secret keycloak-erasure-client-secret)"));
        }

        if (failures.Count == 0)
        {
            return ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(failures);
    }

    private static string Failure(string key, string problem) => $"{KeycloakAccountsSettings.Section}:{key} {problem}.";
}
