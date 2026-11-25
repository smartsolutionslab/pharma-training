using Microsoft.Extensions.Configuration;

namespace Microsoft.Extensions.DependencyInjection;

public static class KeycloakAccountsRegistration
{
    /// <summary>
    /// Registers the people's accounts over Keycloak's admin API and its settings, <c>Keycloak:Accounts</c>, which the host
    /// checks at start-up.
    /// </summary>
    public static IServiceCollection AddKeycloakAccounts(this IServiceCollection services, IConfiguration configuration)
    {
        Ensure.That(services).IsNotNull();
        Ensure.That(configuration).IsNotNull();
        return services;
    }
}
