using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure;

namespace Microsoft.Extensions.DependencyInjection;

public static class KeycloakAccountsRegistration
{
    /// <summary>
    /// Registers the people's accounts over Keycloak's admin API and its settings, <c>Keycloak:Accounts</c>, which the host
    /// checks at start-up. <see cref="KeycloakAccounts"/> is a typed client of the host's <c>IHttpClientFactory</c>, so it
    /// has the host's resilience pipeline, which retries the read and the delete; the token request, a POST, gets one
    /// attempt (owner decision A on #115).
    /// </summary>
    public static IServiceCollection AddKeycloakAccounts(this IServiceCollection services, IConfiguration configuration)
    {
        Ensure.That(services).IsNotNull();
        Ensure.That(configuration).IsNotNull();

        services.AddOptions<KeycloakAccountsSettings>()
            .Bind(configuration.GetSection(KeycloakAccountsSettings.Section))
            .ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<KeycloakAccountsSettings>, KeycloakAccountsSettingsValidation>());
        services.AddHttpClient<IAccounts, KeycloakAccounts>();
        return services;
    }
}
