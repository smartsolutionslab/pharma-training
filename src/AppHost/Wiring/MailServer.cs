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
/// Vault as <c>mail-connection-string</c>, as a Key Vault reference.
/// </summary>
public sealed class MailServer
{
    private const string Name = "mail";

    /// <summary>
    /// The submission port with STARTTLS (RFC 6409), which Azure Communication Services' SMTP relay answers on, as most relays
    /// do. <c>smtp-host</c> names the host alone, as Keycloak's <c>SMTP_HOST</c> does, so the port is the AppHost's.
    /// </summary>
    private const string SubmissionPort = "587";

    private readonly IResourceBuilder<ContainerResource>? mailpit;

    private readonly IResourceBuilder<IResourceWithConnectionString>? connectionString;

    private readonly PublishedSmtp? published;

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

    /// <summary>The sender: <c>mail-from</c> in run mode, Keycloak's <c>smtp-from</c> once published.</summary>
    public IResourceBuilder<ParameterResource> From { get; }

    public static MailServer Add(IDistributedApplicationBuilder builder, DeploymentSecrets secrets)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(secrets);

        if (!secrets.IsPublishing)
        {
            var mailpit = builder.AddContainer("mailpit", "axllent/mailpit", "v1.33")
                .WithHttpEndpoint(port: 8025, targetPort: 8025)
                .WithEndpoint(targetPort: 1025, name: "smtp")
                .WithHttpHealthCheck("/livez");
            // The services run on the host, not in the containers' network: they send to Mailpit's allocated port, without credentials.
            var connectionString = builder.AddConnectionString(Name, ReferenceExpression.Create($"smtp://{mailpit.GetEndpoint("smtp").Property(EndpointProperty.HostAndPort)}"));
            return new MailServer(mailpit, connectionString, builder.AddParameter("mail-from", "noreply@example.com"));
        }

        var host = builder.AddParameter("smtp-host");
        var from = builder.AddParameter("smtp-from");
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

        if (published is null)
        {
            return keycloak.WaitFor(Mailpit);
        }

        return keycloak
            .WithEnvironment("SMTP_HOST", published.Host)
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

    private IResourceBuilder<ContainerResource> Mailpit =>
        mailpit ?? throw new InvalidOperationException("Mailpit exists in run mode only.");

    private sealed record PublishedSmtp(
        IResourceBuilder<ParameterResource> Host,
        IResourceBuilder<ParameterResource> From,
        IResourceBuilder<ParameterResource> UserName,
        IResourceBuilder<ParameterResource> Password);
}
