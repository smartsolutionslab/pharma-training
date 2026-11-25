using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Azure;

namespace Aspire.Hosting;

/// <summary>
/// The one SMTP server Keycloak and the services that send mail send through (docs/adr/0004, docs/adr/0005). In run mode it is Mailpit,
/// whose inbox is http://localhost:8025: the realm points Keycloak at it, and the services that send mail (their project says
/// <c>&lt;SendsMail&gt;true&lt;/SendsMail&gt;</c>) read its allocated port as the
/// connection string <c>mail</c>, without credentials, and send from <c>mail-from</c>, <c>noreply@example.com</c> by
/// default. Published, it is the server the deployment names in <c>smtp-host</c>, with the user and password
/// <c>smtp-username</c> / <c>smtp-password</c> from the Key Vault, and the sender <c>smtp-from</c>: Keycloak reads them one
/// by one, and those services read the connection string the AppHost composes from the same parameters, stored in the Key
/// Vault as <c>mail-connection-string</c>, as a Key Vault reference. Published to staging, it is a Mailpit of staging's
/// own instead (#130, the owner's decisions on it): the staging end-to-end run reads the invitation mails from its inbox,
/// which answers outside the environment behind Mailpit's basic auth (<c>mailpit-ui-auth</c>, from the Key Vault), while
/// its SMTP port answers inside the environment alone. Nothing there would trust a certificate Mailpit presents, so
/// Keycloak and the services send to it without TLS and without credentials, which the environment's peer-traffic
/// encryption carries; the services name it in <c>Mail:UnencryptedHost</c>, the one exception to their STARTTLS rule.
/// Production keeps the relay, with STARTTLS and its credentials.
/// </summary>
public sealed class MailServer
{
    private const string Name = "mail";

    /// <summary>
    /// The submission port with STARTTLS (RFC 6409), which Azure Communication Services' SMTP relay answers on, as most relays
    /// do. <c>smtp-host</c> names the host alone, as Keycloak's <c>SMTP_HOST</c> does, so the port is the AppHost's.
    /// </summary>
    private const string SubmissionPort = "587";

    private const int MailpitSmtpPort = 1025;

    private const string MailpitImage = "axllent/mailpit";

    // An exact patch, the same in run mode and on staging, so what runs changes only with a change here (#442).
    private const string MailpitTag = "v1.33.1";

    private readonly IResourceBuilder<ContainerResource>? mailpit;

    private readonly IResourceBuilder<IResourceWithConnectionString>? connectionString;

    private readonly PublishedSmtp? published;

    private readonly IResourceBuilder<ContainerResource>? stagingMailpit;

    private MailServer(IResourceBuilder<ContainerResource> mailpit, IResourceBuilder<IResourceWithConnectionString> connectionString, IResourceBuilder<ParameterResource> from)
    {
        this.mailpit = mailpit;
        this.connectionString = connectionString;
        From = from;
    }

    private MailServer(PublishedSmtp published)
    {
        this.published = published;
        From = published.From;
    }

    private MailServer(IResourceBuilder<ContainerResource> stagingMailpit, IResourceBuilder<ParameterResource> from)
    {
        this.stagingMailpit = stagingMailpit;
        From = from;
    }

    /// <summary>The sender: <c>mail-from</c> in run mode, Keycloak's <c>smtp-from</c> once published.</summary>
    public IResourceBuilder<ParameterResource> From { get; }

    public static MailServer Add(IDistributedApplicationBuilder builder, DeploymentSecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(secrets);

        if (!secrets.IsPublishing)
        {
            var mailpit = builder.AddContainer("mailpit", MailpitImage, MailpitTag)
                .WithHttpEndpoint(port: 8025, targetPort: 8025)
                .WithEndpoint(targetPort: 1025, name: "smtp")
                .WithHttpHealthCheck("/livez");
            // The services run on the host, not in the containers' network: they send to Mailpit's allocated port, without credentials.
            var connectionString = builder.AddConnectionString(Name, ReferenceExpression.Create($"smtp://{mailpit.GetEndpoint("smtp").Property(EndpointProperty.HostAndPort)}"));
            return new MailServer(mailpit, connectionString, builder.AddParameter("mail-from", "noreply@example.com"));
        }

        var from = builder.AddParameter("smtp-from");
        if (builder.Environment.EnvironmentName == DatabaseServerSize.Staging)
        {
            return AddStagingMailpit(builder, secrets, from);
        }

        var host = builder.AddParameter("smtp-host");
        var userName = secrets.Secret("smtp-username");
        var password = secrets.Secret("smtp-password");
        // smtp:// to a host that is not loopback requires STARTTLS (src/BuildingBlocks/Mail/SmtpSettings.cs), so neither the
        // password nor a mail with its link travels in clear. Main.bicep does not URI-escape a value it assembles (Aspire
        // 13.6.5 drops a reference expression's :uri format there), so the SMTP user and password must be URI-safe
        // (docs/adr/0005), as the broker's are: Azure Communication Services' <resource>.<app id>.<tenant id> user is.
        secrets.Store($"{Name}-connection-string", ReferenceExpression.Create($"smtp://{userName.Resource}:{password.Resource}@{host.Resource}:{SubmissionPort}"));
        return new MailServer(new PublishedSmtp(host, from, userName, password));
    }

    /// <summary>
    /// Keycloak's mail server: in run mode the realm names Mailpit, and Keycloak waits for it; published, the SMTP host,
    /// sender, user and password, the last two as Key Vault references.
    /// </summary>
    internal IResourceBuilder<ContainerResource> ServeKeycloak(IResourceBuilder<ContainerResource> keycloak, DeploymentSecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(keycloak);
        ArgumentNullException.ThrowIfNull(secrets);

        if (stagingMailpit is not null)
        {
            return keycloak
                .WithEnvironment("SMTP_HOST", stagingMailpit.GetEndpoint("smtp").Property(EndpointProperty.Host))
                .WithEnvironment("SMTP_PORT", MailpitSmtpPort.ToString(System.Globalization.CultureInfo.InvariantCulture))
                .WithEnvironment("SMTP_STARTTLS", "false")
                .WithEnvironment("SMTP_AUTH", "false")
                .WithEnvironment("SMTP_FROM", From)
                .WithEnvironment("SMTP_USERNAME", string.Empty)
                .WithEnvironment("SMTP_PASSWORD", string.Empty);
        }

        if (published is null)
        {
            return keycloak.WaitFor(Mailpit);
        }

        return keycloak
            .WithEnvironment("SMTP_HOST", published.Host)
            .WithEnvironment("SMTP_PORT", SubmissionPort)
            .WithEnvironment("SMTP_STARTTLS", "true")
            .WithEnvironment("SMTP_AUTH", "true")
            .WithEnvironment("SMTP_FROM", published.From)
            .WithSecret("SMTP_USERNAME", published.UserName, secrets)
            .WithSecret("SMTP_PASSWORD", published.Password, secrets);
    }

    /// <summary>
    /// A service's mail: the connection string <c>mail</c> (in run mode as a reference, published as the Key Vault secret
    /// <see cref="Add"/> stored) and the sender, <c>Mail:From</c>.
    /// </summary>
    internal IResourceBuilder<T> ServeService<T>(IResourceBuilder<T> service, DeploymentSecrets secrets)
        where T : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(service);
        ArgumentNullException.ThrowIfNull(secrets);

        if (stagingMailpit is not null)
        {
            var smtp = stagingMailpit.GetEndpoint("smtp");
            return service
                .WithEnvironment($"ConnectionStrings__{Name}", ReferenceExpression.Create($"smtp://{smtp.Property(EndpointProperty.HostAndPort)}"))
                .WithEnvironment("Mail__UnencryptedHost", smtp.Property(EndpointProperty.Host))
                .WithEnvironment("Mail__From", From);
        }

        if (published is null)
        {
            return service
                .WithReference(connectionString ?? throw new InvalidOperationException($"{Name} has no connection string in run mode."))
                .WithEnvironment("Mail__From", From);
        }

        IAzureKeyVaultSecretReference stored = secrets.Stored($"{Name}-connection-string").Resource;
        return service
            .WithEnvironment($"ConnectionStrings__{Name}", stored)
            .WithEnvironment("Mail__From", From);
    }

    /// <summary>
    /// Staging's Mailpit (#130): its inbox at https://mailpit.&lt;the environment's domain&gt; behind the basic auth
    /// <c>mailpit-ui-auth</c> (<c>user:password</c>, a Key Vault secret), its SMTP port inside the environment alone, one
    /// replica and no volume: a restart empties the inbox, which holds the end-to-end run's mails alone.
    /// </summary>
    private static MailServer AddStagingMailpit(IDistributedApplicationBuilder builder, DeploymentSecrets secrets, IResourceBuilder<ParameterResource> from)
    {
        var inboxAuth = secrets.Secret("mailpit-ui-auth");
        var mailpit = builder.AddContainer("mailpit", MailpitImage, MailpitTag)
            .WithHttpEndpoint(targetPort: 8025, name: "http")
            .WithExternalHttpEndpoints()
            .WithEndpoint(targetPort: MailpitSmtpPort, name: "smtp", scheme: "tcp")
            .WithSecret("MP_UI_AUTH", inboxAuth, secrets)
            .WithSingleReplica();
        return new MailServer(mailpit, from);
    }

    private IResourceBuilder<ContainerResource> Mailpit =>
        mailpit ?? throw new InvalidOperationException("Mailpit exists in run mode only.");

    private sealed record PublishedSmtp(
        IResourceBuilder<ParameterResource> Host,
        IResourceBuilder<ParameterResource> From,
        IResourceBuilder<ParameterResource> UserName,
        IResourceBuilder<ParameterResource> Password);
}
