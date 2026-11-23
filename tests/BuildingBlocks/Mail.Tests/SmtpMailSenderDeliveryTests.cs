using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Time.Testing;
using NSubstitute.ExceptionExtensions;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail.Tests;

/// <summary>
/// Whether a failed send may have reached the server: never before the client's <c>SendAsync</c> started (connect, TLS,
/// login, the bound firing there), always once it has, so a caller withdraws a link only from a mail that cannot be out.
/// </summary>
public class SmtpMailSenderDeliveryTests
{
    private const string Recipient = "anna@example.org";

    private const string ServerText = "only-the-server-wrote-this";

    private static readonly OutgoingMail Invitation = new(Recipient, "Einladung", "Text");

    [Fact]
    public async Task A_connection_that_fails_before_the_send_started_cannot_have_delivered_the_mail()
    {
        var client = Substitute.For<ISmtpClient>();
        client.ConnectAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<SecureSocketOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException(ServerText));

        var failed = await Should.ThrowAsync<MailNotSentException>(() => Sender(client, new FakeTimeProvider()).SendAsync(Invitation, TestContext.Current.CancellationToken));

        failed.MayHaveBeenDelivered.ShouldBeFalse();
    }

    [Fact]
    public async Task A_login_the_server_refuses_before_the_send_started_cannot_have_delivered_the_mail()
    {
        var client = Substitute.For<ISmtpClient>();
        client.AuthenticateAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new SmtpCommandException(SmtpErrorCode.UnexpectedStatusCode, SmtpStatusCode.AuthenticationInvalidCredentials, ServerText));

        var failed = await Should.ThrowAsync<MailNotSentException>(
            () => Sender(client, new FakeTimeProvider(), $"smtp://user:{Guid.CreateVersion7():N}@127.0.0.1:25").SendAsync(Invitation, TestContext.Current.CancellationToken));

        failed.MayHaveBeenDelivered.ShouldBeFalse();
    }

    [Fact]
    public async Task A_bound_that_fires_before_the_send_started_cannot_have_delivered_the_mail()
    {
        var time = new FakeTimeProvider();
        var client = Substitute.For<ISmtpClient>();
        client.ConnectAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<SecureSocketOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                time.Advance(TimeSpan.FromSeconds(11));
                return Task.FromException(new IOException(ServerText));
            });

        var failed = await TimedOutAsync(Sender(client, time));

        failed.MayHaveBeenDelivered.ShouldBeFalse();
    }

    /// <summary>
    /// A transport failure once the send started may have delivered the mail; the server's own refusal of DATA says it
    /// did not take it (review B1, rule a).
    /// </summary>
    [Theory]
    [InlineData("a dropped connection", true)]
    [InlineData("a refused DATA", false)]
    public async Task A_failure_once_the_send_started_may_have_delivered_the_mail_unless_the_server_refused_it(string failure, bool mayHaveBeenDelivered)
    {
        var client = Substitute.For<ISmtpClient>();
        client.SendAsync(Arg.Any<MimeKit.MimeMessage>(), Arg.Any<CancellationToken>(), Arg.Any<ITransferProgress>())
            .ThrowsAsync(failure == "a dropped connection"
                ? new IOException(ServerText)
                : new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.TransactionFailed, ServerText));

        var failed = await Should.ThrowAsync<MailNotSentException>(() => Sender(client, new FakeTimeProvider()).SendAsync(Invitation, TestContext.Current.CancellationToken));

        failed.MayHaveBeenDelivered.ShouldBe(mayHaveBeenDelivered);
    }

    /// <summary>
    /// The server answering MAIL FROM, RCPT TO or DATA with a refusal says it did not take the mail, although the
    /// client's send had started: the caller may withdraw its link (review B1, rule a).
    /// </summary>
    [Theory]
    [InlineData(SmtpErrorCode.SenderNotAccepted, SmtpStatusCode.MailboxUnavailable)]
    [InlineData(SmtpErrorCode.RecipientNotAccepted, SmtpStatusCode.MailboxUnavailable)]
    [InlineData(SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.TransactionFailed)]
    public async Task A_command_the_server_refuses_once_the_send_started_cannot_have_delivered_the_mail(SmtpErrorCode step, SmtpStatusCode status)
    {
        var client = Substitute.For<ISmtpClient>();
        client.SendAsync(Arg.Any<MimeKit.MimeMessage>(), Arg.Any<CancellationToken>(), Arg.Any<ITransferProgress>())
            .ThrowsAsync(new SmtpCommandException(step, status, ServerText));

        var failed = await Should.ThrowAsync<MailNotSentException>(() => Sender(client, new FakeTimeProvider()).SendAsync(Invitation, TestContext.Current.CancellationToken));

        failed.MayHaveBeenDelivered.ShouldBeFalse($"the server refused {step}, so it does not hold the mail");
    }

    /// <summary>
    /// A setting that is missing or unusable fails before anything is handed to a server: the mail cannot be out
    /// (review B1, rule b).
    /// </summary>
    [Theory]
    [InlineData("no sender address")]
    [InlineData("no server")]
    public async Task A_setting_that_fails_before_the_send_cannot_have_delivered_the_mail(string missing)
    {
        var client = Substitute.For<ISmtpClient>();
        var settings = missing == "no sender address"
            ? Settings.Of(("ConnectionStrings:mail", "smtp://127.0.0.1:25"))
            : Settings.Of(("Mail:From", "noreply@example.com"));
        var sender = new SmtpMailSender(settings, new FakeTimeProvider(), () => client);

        var failed = await Should.ThrowAsync<MailNotSentException>(() => sender.SendAsync(Invitation, TestContext.Current.CancellationToken));

        failed.MayHaveBeenDelivered.ShouldBeFalse();
        await client.DidNotReceive().SendAsync(Arg.Any<MimeKit.MimeMessage>(), Arg.Any<CancellationToken>(), Arg.Any<ITransferProgress>());
    }

    /// <summary>
    /// The caller's cancellation before the send started cannot have delivered the mail, and says so as the other
    /// failures before the send do (review B1, rule b).
    /// </summary>
    [Fact]
    public async Task The_callers_cancellation_before_the_send_cannot_have_delivered_the_mail()
    {
        var client = Substitute.For<ISmtpClient>();
        using var caller = new CancellationTokenSource();
        await caller.CancelAsync();

        var failed = await FailureOfAsync(Sender(client, new FakeTimeProvider()), caller.Token);

        MayHaveBeenDelivered(failed).ShouldBe(false, $"the caller's cancellation surfaced as {failed.GetType().Name}, which says nothing about delivery");
        await client.DidNotReceive().SendAsync(Arg.Any<MimeKit.MimeMessage>(), Arg.Any<CancellationToken>(), Arg.Any<ITransferProgress>());
    }

    [Fact]
    public async Task A_bound_that_fires_once_the_send_started_may_have_delivered_the_mail()
    {
        var time = new FakeTimeProvider();
        var client = Substitute.For<ISmtpClient>();
        client.SendAsync(Arg.Any<MimeKit.MimeMessage>(), Arg.Any<CancellationToken>(), Arg.Any<ITransferProgress>())
            .Returns(_ =>
            {
                time.Advance(TimeSpan.FromSeconds(11));
                return Task.FromException<string>(new IOException(ServerText));
            });

        var failed = await TimedOutAsync(Sender(client, time));

        failed.MayHaveBeenDelivered.ShouldBeTrue();
    }

    [Fact]
    public void A_failure_that_names_nothing_may_have_delivered_the_mail()
    {
        // Whoever cannot tell keeps the link: the server may hold the mail.
        new MailNotSentException("The mail was not sent: IOException.").MayHaveBeenDelivered.ShouldBeTrue();
        new MailNotSentException().MayHaveBeenDelivered.ShouldBeTrue();
    }

    /// <summary>
    /// The bound's cancellation as the caller's own <c>await</c> sees it. Should.ThrowAsync reports a cancelled task with a
    /// cancellation of its own, not the one the sender threw, so it is awaited here directly.
    /// </summary>
    private static async Task<MailSendTimedOutException> TimedOutAsync(SmtpMailSender sender)
    {
        try
        {
            await sender.SendAsync(Invitation, TestContext.Current.CancellationToken);
        }
        catch (MailSendTimedOutException timedOut)
        {
            return timedOut;
        }

        throw new ShouldAssertException("The send completed although the bound fired.");
    }

    /// <summary>Whatever the send threw, awaited directly so a cancelled task surfaces with its own exception.</summary>
    private static async Task<Exception> FailureOfAsync(SmtpMailSender sender, CancellationToken cancellationToken)
    {
        try
        {
            await sender.SendAsync(Invitation, cancellationToken);
        }
        catch (Exception failed)
        {
            return failed;
        }

        throw new ShouldAssertException("The send completed although it should have failed.");
    }

    /// <summary>What the failure says about delivery; <see langword="null"/> for an exception that does not say.</summary>
    private static bool? MayHaveBeenDelivered(Exception failed) => failed switch
    {
        MailNotSentException notSent => notSent.MayHaveBeenDelivered,
        MailSendTimedOutException timedOut => timedOut.MayHaveBeenDelivered,
        _ => null,
    };

    private static SmtpMailSender Sender(ISmtpClient client, TimeProvider time, string server = "smtp://127.0.0.1:25") =>
        new(Settings.Of(("ConnectionStrings:mail", server), ("Mail:From", "noreply@example.com")), time, () => client);
}
