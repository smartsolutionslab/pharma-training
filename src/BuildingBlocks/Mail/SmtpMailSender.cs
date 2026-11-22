using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Configuration;
using MimeKit;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;

/// <summary>
/// Sends mail over SMTP with MailKit, one connection per mail, to the server in <c>ConnectionStrings:mail</c> from
/// <c>Mail:From</c>. Both are read at every send, so a host without them starts, fails only where it mails, and sends
/// once they are supplied.
/// </summary>
public sealed class SmtpMailSender : IMailSender
{
    private const string FromKey = "Mail:From";

    /// <summary>The send runs inside a request; the whole of it, not each step, ends after this long.</summary>
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    private readonly IConfiguration configuration;

    private readonly TimeProvider time;

    public SmtpMailSender(IConfiguration configuration)
        : this(configuration, TimeProvider.System)
    {
    }

    /// <param name="configuration">Where the server and the sender address are read at each send.</param>
    /// <param name="time">The clock the whole send's bound is measured on.</param>
    public SmtpMailSender(IConfiguration configuration, TimeProvider time)
    {
        Ensure.That(configuration).IsNotNull();
        Ensure.That(time).IsNotNull();
        this.configuration = configuration;
        this.time = time;
    }

    /// <summary>
    /// Sends the mail within <see cref="Bound"/>. The caller's token is honoured only before the send starts: a mail half
    /// handed to the server is finished or fails on the bound, never abandoned because the request went away.
    /// </summary>
    public async Task SendAsync(OutgoingMail mail, CancellationToken cancellationToken)
    {
        Ensure.That(mail).IsNotNull();
        cancellationToken.ThrowIfCancellationRequested();

        using var message = Compose(mail, From(configuration));
        var settings = SmtpSettings.From(configuration);
        using var bound = new CancellationTokenSource(Bound, time);
        using var client = new SmtpClient { Timeout = (int)Bound.TotalMilliseconds };
        try
        {
            await client.ConnectAsync(settings.Host, settings.Port, settings.Security, bound.Token);
            if (settings.User is { } user)
            {
                await client.AuthenticateAsync(user, settings.Password ?? string.Empty, bound.Token);
            }

            await client.SendAsync(message, bound.Token);
            await client.DisconnectAsync(quit: true, bound.Token);
        }
        catch (SmtpCommandException refused)
        {
            // The server's text (550 5.1.1 <anna@example.org>: ...) stays out of the exception, its logs and dead letters.
            throw new MailNotSentException($"The SMTP server refused the mail at {refused.ErrorCode} with {(int)refused.StatusCode} {refused.StatusCode}.");
        }
        catch (Exception failed) when (!bound.IsCancellationRequested && IsTransportFailure(failed))
        {
            // A reply the client cannot read, a failed TLS handshake or login, or a dropped connection may carry the
            // server's text or the recipient as well; only the kind of failure goes on. The bound's own cancellation passes.
            throw new MailNotSentException($"The mail was not sent: {failed.GetType().Name}.");
        }
    }

    private static bool IsTransportFailure(Exception failed) =>
        failed is SmtpProtocolException or SslHandshakeException or AuthenticationException or IOException;

    /// <summary>
    /// The message, its headers set only through MimeKit, so no subject or address can add a header. The recipient is
    /// the mail's address exactly as it is: one that would parse to another mailbox (a display name, a comment) is refused.
    /// </summary>
    internal static MimeMessage Compose(OutgoingMail mail, MailboxAddress from)
    {
        Ensure.That(mail).IsNotNull();
        Ensure.That(from).IsNotNull();
        Ensure.That(mail.To, nameof(mail)).Satisfies(IsBareAddress, "Die Empfängeradresse ist keine einfache E-Mail-Adresse.");

        var message = new MimeMessage();
        message.From.Add(from);
        message.To.Add(new MailboxAddress(string.Empty, mail.To));
        message.Subject = mail.Subject;
        message.Body = new TextPart(MimeKit.Text.TextFormat.Plain) { Text = mail.Text };
        return message;
    }

    private static bool IsBareAddress(string to) =>
        MailboxAddress.TryParse(to, out var parsed) && string.IsNullOrEmpty(parsed.Name) && string.Equals(parsed.Address, to, StringComparison.Ordinal);

    internal static MailboxAddress From(IConfiguration configuration)
    {
        if (!MailboxAddress.TryParse(configuration[FromKey], out var address))
        {
            throw new InvalidOperationException($"The sender address is missing or unusable: set {FromKey} to an e-mail address.");
        }

        return address;
    }
}
