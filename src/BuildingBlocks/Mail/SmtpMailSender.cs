using MailKit.Net.Smtp;
using Microsoft.Extensions.Configuration;
using MimeKit;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;

/// <summary>
/// Sends mail over SMTP with MailKit, one connection per mail, to the server in <c>ConnectionStrings:mail</c> from
/// <c>Mail:From</c>. Both are read at the first send, so a host without them starts and fails only where it mails.
/// </summary>
public sealed class SmtpMailSender : IMailSender
{
    private const string FromKey = "Mail:From";

    /// <summary>The send runs inside a request; MailKit's own default would let it wait two minutes.</summary>
    private const int TimeoutMilliseconds = 10_000;

    private readonly Lazy<MailboxAddress> from;

    private readonly Lazy<SmtpSettings> server;

    public SmtpMailSender(IConfiguration configuration)
    {
        Ensure.That(configuration).IsNotNull();
        from = new Lazy<MailboxAddress>(() => From(configuration));
        server = new Lazy<SmtpSettings>(() => SmtpSettings.From(configuration));
    }

    /// <summary>Stub for the red tests of issue #9 (R5): the clock the whole send's bound is measured on, not used yet.</summary>
    public SmtpMailSender(IConfiguration configuration, TimeProvider time)
        : this(configuration)
    {
        Ensure.That(time).IsNotNull();
    }

    public async Task SendAsync(OutgoingMail mail, CancellationToken cancellationToken)
    {
        Ensure.That(mail).IsNotNull();

        using var message = Compose(mail, from.Value);
        var settings = server.Value;
        using var client = new SmtpClient { Timeout = TimeoutMilliseconds };
        await client.ConnectAsync(settings.Host, settings.Port, settings.Security, cancellationToken);
        if (settings.User is { } user)
        {
            await client.AuthenticateAsync(user, settings.Password ?? string.Empty, cancellationToken);
        }

        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(quit: true, cancellationToken);
    }

    /// <summary>The message, its headers set only through MimeKit, so no subject or address can add a header.</summary>
    internal static MimeMessage Compose(OutgoingMail mail, MailboxAddress from)
    {
        Ensure.That(mail).IsNotNull();
        Ensure.That(from).IsNotNull();

        var message = new MimeMessage();
        message.From.Add(from);
        message.To.Add(MailboxAddress.Parse(mail.To));
        message.Subject = mail.Subject;
        message.Body = new TextPart(MimeKit.Text.TextFormat.Plain) { Text = mail.Text };
        return message;
    }

    private static MailboxAddress From(IConfiguration configuration)
    {
        if (!MailboxAddress.TryParse(configuration[FromKey], out var address))
        {
            throw new InvalidOperationException($"The sender address is missing or unusable: set {FromKey} to an e-mail address.");
        }

        return address;
    }
}
