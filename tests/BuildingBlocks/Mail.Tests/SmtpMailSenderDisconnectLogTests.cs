using MailKit;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Time.Testing;
using NSubstitute.ExceptionExtensions;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail.Tests;

/// <summary>
/// A failure while disconnecting after the server took the mail fails nothing, so it is logged: a warning naming only
/// the kind of failure, never the server's address, its text or the recipient, and without the exception that carries them.
/// </summary>
public class SmtpMailSenderDisconnectLogTests
{
    private const string Recipient = "anna@example.org";

    private const string ServerText = "only-the-server-wrote-this";

    private const string ServerAddress = "smtp.example.net:587";

    private static readonly OutgoingMail Invitation = new(Recipient, "Einladung", "Text");

    [Fact]
    public async Task A_failure_while_disconnecting_after_the_mail_was_sent_is_logged_as_a_warning_naming_only_its_kind()
    {
        var client = Substitute.For<ISmtpClient>();
        client.DisconnectAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException($"{ServerAddress} answered {ServerText} for {Recipient}"));
        var logger = new FakeLogger<SmtpMailSender>();
        var sender = new SmtpMailSender(
            Settings.Of(("ConnectionStrings:mail", "smtp://127.0.0.1:25"), ("Mail:From", "noreply@example.com")),
            new FakeTimeProvider(),
            () => client,
            logger);

        await sender.SendAsync(Invitation, TestContext.Current.CancellationToken);

        var record = logger.Collector.GetSnapshot().ShouldHaveSingleItem();
        record.Level.ShouldBe(LogLevel.Warning);
        record.Message.ShouldContain(nameof(IOException), Case.Sensitive);
        record.Message.ShouldNotContain(ServerText, Case.Insensitive);
        record.Message.ShouldNotContain(Recipient, Case.Insensitive);
        record.Message.ShouldNotContain("smtp.example.net", Case.Insensitive);
        record.Exception.ShouldBeNull();
        await client.Received(1).SendAsync(Arg.Any<MimeKit.MimeMessage>(), Arg.Any<CancellationToken>(), Arg.Any<ITransferProgress>());
    }

    [Fact]
    public async Task A_mail_sent_and_disconnected_cleanly_logs_nothing()
    {
        var client = Substitute.For<ISmtpClient>();
        var logger = new FakeLogger<SmtpMailSender>();
        var sender = new SmtpMailSender(
            Settings.Of(("ConnectionStrings:mail", "smtp://127.0.0.1:25"), ("Mail:From", "noreply@example.com")),
            new FakeTimeProvider(),
            () => client,
            logger);

        await sender.SendAsync(Invitation, TestContext.Current.CancellationToken);

        logger.Collector.GetSnapshot().ShouldBeEmpty();
    }
}
