using MailKit.Net.Smtp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MimeKit;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;

/// <summary>
/// Sends mail over SMTP with MailKit, one connection per mail, to the server in <c>ConnectionStrings:mail</c> from
/// <c>Mail:From</c>. Both are read at every send, so a host without them starts, fails only where it mails, and sends
/// once they are supplied.
/// </summary>
public sealed partial class SmtpMailSender : IMailSender
{
    private const string FromKey = "Mail:From";

    /// <summary>The send runs inside a request; the whole of it, not each step, ends after this long.</summary>
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(10);

    private readonly IConfiguration configuration;

    private readonly TimeProvider time;

    private readonly Func<ISmtpClient> clients;

    private readonly ILogger<SmtpMailSender> logger;

    public SmtpMailSender(IConfiguration configuration)
        : this(configuration, TimeProvider.System)
    {
    }

    /// <param name="configuration">Where the server and the sender address are read at each send.</param>
    /// <param name="time">The clock the whole send's bound is measured on.</param>
    public SmtpMailSender(IConfiguration configuration, TimeProvider time)
        : this(configuration, time, NullLogger<SmtpMailSender>.Instance)
    {
    }

    /// <param name="configuration">Where the server and the sender address are read at each send.</param>
    /// <param name="time">The clock the whole send's bound is measured on.</param>
    /// <param name="logger">Where a failure after the mail was sent is written, since it fails nothing.</param>
    public SmtpMailSender(IConfiguration configuration, TimeProvider time, ILogger<SmtpMailSender> logger)
        : this(configuration, time, () => new SmtpClient(), logger)
    {
    }

    /// <param name="configuration">Where the server and the sender address are read at each send.</param>
    /// <param name="time">The clock the whole send's bound is measured on.</param>
    /// <param name="clients">Makes the client of one send; a test gives one whose failures it chooses.</param>
    internal SmtpMailSender(IConfiguration configuration, TimeProvider time, Func<ISmtpClient> clients)
        : this(configuration, time, clients, NullLogger<SmtpMailSender>.Instance)
    {
    }

    /// <param name="configuration">Where the server and the sender address are read at each send.</param>
    /// <param name="time">The clock the whole send's bound is measured on.</param>
    /// <param name="clients">Makes the client of one send; a test gives one whose failures it chooses.</param>
    /// <param name="logger">Where a failure after the mail was sent is written, since it fails nothing.</param>
    internal SmtpMailSender(IConfiguration configuration, TimeProvider time, Func<ISmtpClient> clients, ILogger<SmtpMailSender> logger)
    {
        Ensure.That(configuration).IsNotNull();
        Ensure.That(time).IsNotNull();
        Ensure.That(clients).IsNotNull();
        Ensure.That(logger).IsNotNull();
        this.configuration = configuration;
        this.time = time;
        this.clients = clients;
        this.logger = logger;
    }

    /// <summary>
    /// Sends the mail within <see cref="Bound"/>. The caller's token is honoured only before the send starts: a mail half
    /// handed to the server is finished or fails on the bound, never abandoned because the request went away. Once the
    /// client's send has returned, the server has taken the mail: a failure while disconnecting afterwards, the bound
    /// firing included, is logged and the mail counts as sent, so nothing retries a mail the recipient already has.
    /// </summary>
    public async Task SendAsync(OutgoingMail mail, CancellationToken cancellationToken)
    {
        Ensure.That(mail).IsNotNull();
        cancellationToken.ThrowIfCancellationRequested();

        using var message = Compose(mail, From(configuration));
        var settings = SmtpSettings.From(configuration);
        using var bound = new CancellationTokenSource(Bound, time);
        using var client = clients();
        client.Timeout = (int)Bound.TotalMilliseconds;
        try
        {
            await client.ConnectAsync(settings.Host, settings.Port, settings.Security, bound.Token);
            if (settings.User is { } user)
            {
                await client.AuthenticateAsync(user, settings.Password ?? string.Empty, bound.Token);
            }

            await client.SendAsync(message, bound.Token);
        }
        catch (Exception) when (bound.IsCancellationRequested)
        {
            // Once the bound has fired, MailKit may answer it with an IOException or anything else carrying the server's
            // text; whatever it threw, the send ran out of time, and only the bound's own cancellation goes on.
            throw new OperationCanceledException(bound.Token);
        }
        catch (SmtpCommandException refused)
        {
            // The server's text (550 5.1.1 <anna@example.org>: ...) stays out of the exception, its logs and dead letters.
            throw new MailNotSentException($"The SMTP server refused the mail at {refused.ErrorCode} with {(int)refused.StatusCode} {refused.StatusCode}.");
        }
        catch (Exception failed)
        {
            // Whatever else the client throws before the bound fires (a reply it cannot read, a failed TLS handshake or
            // login, a dropped connection, a timeout, an operation it refuses) may carry the server's address, its text or
            // the recipient; only the kind of failure goes on, without the exception itself.
            throw new MailNotSentException($"The mail was not sent: {failed.GetType().Name}.");
        }

        await DisconnectAfterSendAsync(client, bound.Token);
    }

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

    /// <summary>
    /// Says goodbye to a server that has taken the mail. Whatever fails here fails nothing: the mail is sent, and only the
    /// kind of failure is logged, since the exception may carry the server's address, its text or the recipient.
    /// </summary>
    private async Task DisconnectAfterSendAsync(ISmtpClient client, CancellationToken bound)
    {
        try
        {
            await client.DisconnectAsync(quit: true, bound);
        }
        catch (Exception failed)
        {
            DisconnectFailedAfterSend(logger, failed.GetType().Name);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The SMTP server took the mail, but disconnecting afterwards failed with {Failure}; the mail counts as sent.")]
    private static partial void DisconnectFailedAfterSend(ILogger logger, string failure);

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
