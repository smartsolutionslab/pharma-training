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

    [Theory]
    [InlineData("a dropped connection")]
    [InlineData("a refused DATA")]
    public async Task A_failure_once_the_send_started_may_have_delivered_the_mail(string failure)
    {
        var client = Substitute.For<ISmtpClient>();
        client.SendAsync(Arg.Any<MimeKit.MimeMessage>(), Arg.Any<CancellationToken>(), Arg.Any<ITransferProgress>())
            .ThrowsAsync(failure == "a dropped connection"
                ? new IOException(ServerText)
                : new SmtpCommandException(SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.TransactionFailed, ServerText));

        var failed = await Should.ThrowAsync<MailNotSentException>(() => Sender(client, new FakeTimeProvider()).SendAsync(Invitation, TestContext.Current.CancellationToken));

        failed.MayHaveBeenDelivered.ShouldBeTrue();
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

    private static SmtpMailSender Sender(ISmtpClient client, TimeProvider time, string server = "smtp://127.0.0.1:25") =>
        new(Settings.Of(("ConnectionStrings:mail", server), ("Mail:From", "noreply@example.com")), time, () => client);
}
