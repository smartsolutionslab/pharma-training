using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>
/// Keycloak's administrator and the mail server it sends verification and reset-password email through. In run mode the
/// administrator is "admin", with a password Aspire generates once and keeps in the AppHost's user secrets, and email lands
/// in Mailpit, as it always did. Published (docs/adr/0005), the deployment sets the administrator, its password and the
/// SMTP server (<see cref="MailServer"/>, which the services send through too); the passwords and the SMTP user are Key
/// Vault secrets Keycloak reads as Key Vault references.
/// </summary>
public static class KeycloakSettings
{
    public static IResourceBuilder<ContainerResource> WithAdministrator(this IResourceBuilder<ContainerResource> keycloak, DeploymentSecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(keycloak);
        ArgumentNullException.ThrowIfNull(secrets);

        var builder = keycloak.ApplicationBuilder;
        if (!secrets.IsPublishing)
        {
            var generated = builder.AddResource(ParameterResourceBuilderExtensions.CreateDefaultPasswordParameter(builder, "keycloak-password"));
            return keycloak
                .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", "admin")
                .WithEnvironment("KC_BOOTSTRAP_ADMIN_PASSWORD", generated);
        }

        var userName = builder.AddParameter("keycloak-admin-username", "admin", publishValueAsDefault: true);
        var password = secrets.Secret("keycloak-admin-password");
        return keycloak
            .WithEnvironment("KC_BOOTSTRAP_ADMIN_USERNAME", userName)
            .WithSecret("KC_BOOTSTRAP_ADMIN_PASSWORD", password, secrets);
    }

    /// <summary>
    /// The SMTP server Keycloak sends through, the one the services send through too (<see cref="MailServer"/>): in run
    /// mode the realm names Mailpit, and Keycloak waits for it; published, <c>SMTP_HOST</c> and <c>SMTP_FROM</c> from their
    /// parameters, <c>SMTP_USERNAME</c> and <c>SMTP_PASSWORD</c> from the Key Vault.
    /// </summary>
    public static IResourceBuilder<ContainerResource> WithMailServer(this IResourceBuilder<ContainerResource> keycloak, MailServer mail, DeploymentSecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(mail);

        return mail.ServeKeycloak(keycloak, secrets);
    }
}
