using System.Net;
using MailKit.Security;
using Microsoft.Extensions.Configuration;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;

/// <summary>
/// The SMTP server in <c>ConnectionStrings:mail</c>: <c>smtp://[user[:password]@]host[:port]</c> or <c>smtps://…</c>.
/// A class and not a record, so that nothing prints <see cref="Password"/>; no error names the value either. A path, query
/// or fragment is refused: an unescaped "/", "?" or "#" in the password would otherwise make the user the host.
/// </summary>
internal sealed class SmtpSettings
{
    private const string ConnectionStringKey = "ConnectionStrings:mail";

    /// <summary>
    /// The one non-loopback host that may take mail without TLS, when no credential goes with it: staging's Mailpit inside
    /// the Container Apps environment, which encrypts the traffic between its apps; set there by the AppHost alone (#130).
    /// </summary>
    private const string UnencryptedHostKey = "Mail:UnencryptedHost";

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
            || string.IsNullOrEmpty(server.Host)
            || server.AbsolutePath is not ("" or "/")
            || server.Query.Length > 0
            || server.Fragment.Length > 0)
        {
            throw new InvalidOperationException(
                $"The SMTP server is missing or unusable: set {ConnectionStringKey} to smtp://[user[:password]@]host[:port] or smtps://….");
        }

        var (user, password) = Credentials(server.UserInfo);
        var secure = server.Scheme == "smtps";
        return new SmtpSettings
        {
            Host = server.DnsSafeHost,
            Port = server.IsDefaultPort || server.Port < 0 ? (secure ? SmtpsPort : SmtpPort) : server.Port,
            User = user,
            Password = password,

            // A password, and a mail with its link, never travel in clear off this machine; only a loopback server
            // without credentials (Mailpit), or the host the AppHost names as unencrypted (staging's Mailpit), may have
            // no TLS at all.
            Security = secure ? SecureSocketOptions.SslOnConnect
                : user is null && (IsLoopback(server) || IsUnencryptedHost(server, configuration[UnencryptedHostKey])) ? SecureSocketOptions.StartTlsWhenAvailable
                : SecureSocketOptions.StartTls,
        };
    }

    /// <summary>The name localhost or a loopback address; a name that merely starts with localhost is not.</summary>
    internal static bool IsLoopback(Uri url) =>
        string.Equals(url.DnsSafeHost, "localhost", StringComparison.OrdinalIgnoreCase)
        || (IPAddress.TryParse(url.DnsSafeHost, out var address) && IPAddress.IsLoopback(address));

    /// <summary>The host is exactly the one named as unencrypted; nothing is when none is named.</summary>
    private static bool IsUnencryptedHost(Uri url, string? unencryptedHost) =>
        !string.IsNullOrWhiteSpace(unencryptedHost)
        && string.Equals(url.DnsSafeHost, unencryptedHost.Trim(), StringComparison.OrdinalIgnoreCase);

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
