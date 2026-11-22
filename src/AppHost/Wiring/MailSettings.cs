using Aspire.Hosting.ApplicationModel;

namespace Aspire.Hosting;

/// <summary>The mail a service sends itself (the invitation, docs/adr/0004), through <see cref="MailServer"/>.</summary>
public static class MailSettings
{
    /// <summary>
    /// The connection string <c>mail</c>, in run mode as a reference to Mailpit's, published as the Key Vault secret
    /// composed from the <c>smtp-*</c> parameters, and the sender, <c>Mail:From</c>.
    /// </summary>
    public static IResourceBuilder<T> WithMail<T>(this IResourceBuilder<T> service, MailServer mail, DeploymentSecrets secrets)
        where T : IResourceWithEnvironment
    {
        ArgumentNullException.ThrowIfNull(mail);

        return mail.ServeService(service, secrets);
    }
}
