using MailKit.Security;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;

/// <summary>
/// The SMTP server in <c>ConnectionStrings:mail</c>: <c>smtp://[user[:password]@]host[:port]</c> or <c>smtps://…</c>.
/// A class and not a record, so that nothing prints <see cref="Password"/>; no error names the value either.
/// </summary>
internal sealed class SmtpSettings
{
    private const string ConnectionStringKey = "ConnectionStrings:mail";

    private const int SmtpPort = 25;

    private const int SmtpsPort = 465;

    private SmtpSettings()
    {
    }

    public string Host { get; private init; } = string.Empty;

    public int Port { get; private init; }

    public string? User { get; private init; }

    public string? Password { get; private init; }

    public SecureSocketOptions Security { get; private init; }

    public static SmtpSettings From(IConfiguration configuration)
    {
        Ensure.That(configuration).IsNotNull();

        if (!Uri.TryCreate(configuration[ConnectionStringKey], UriKind.Absolute, out var server)
            || (server.Scheme != "smtp" && server.Scheme != "smtps")
            || string.IsNullOrEmpty(server.Host))
        {
            throw new InvalidOperationException(
                $"The SMTP server is missing or unusable: set {ConnectionStringKey} to smtp://[user[:password]@]host[:port] or smtps://….");
        }

        var (user, password) = Credentials(server.UserInfo);
        var secure = server.Scheme == "smtps";
        return new SmtpSettings
        {
            Host = server.Host,
            Port = server.IsDefaultPort || server.Port < 0 ? (secure ? SmtpsPort : SmtpPort) : server.Port,
            User = user,
            Password = password,

            // A password never travels in clear; a server without credentials (Mailpit) may have no TLS at all.
            Security = secure ? SecureSocketOptions.SslOnConnect
                : user is not null ? SecureSocketOptions.StartTls
                : SecureSocketOptions.StartTlsWhenAvailable,
        };
    }

    private static (string? User, string? Password) Credentials(string userInfo)
    {
        if (string.IsNullOrEmpty(userInfo))
        {
            return (null, null);
        }

        var separator = userInfo.IndexOf(':', StringComparison.Ordinal);
        if (separator < 0)
        {
            return (Uri.UnescapeDataString(userInfo), null);
        }

        return (Uri.UnescapeDataString(userInfo[..separator]), Uri.UnescapeDataString(userInfo[(separator + 1)..]));
    }
}
