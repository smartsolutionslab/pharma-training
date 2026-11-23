using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Time.Testing;
using NSubstitute.ExceptionExtensions;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail.Tests;

/// <summary>
/// What MailKit's client throws, given through the sender's client factory: every transport failure surfaces as
/// <see cref="MailNotSentException"/> naming only the kind of failure, and once the send's bound has fired, whatever the
/// client throws surfaces as the bound's cancellation, never as the client's own exception.
/// </summary>
public class SmtpMailSenderClientFailureTests
{
    private const string ServerKey = "ConnectionStrings:mail";

    private const string FromKey = "Mail:From";

    private const string Recipient = "anna@example.org";

    /// <summary>Text only the server writes, so a message that holds it carries what the server said.</summary>
    private const string ServerText = "only-the-server-wrote-this";

    /// <summary>The server's address, as MailKit names it in the messages of the exceptions it throws.</summary>
    private const string ServerAddress = "smtp.example.net:587";

    private static readonly OutgoingMail Invitation = new(Recipient, "Einladung", "Text");

    /// <summary>
    /// Exceptions MailKit's client may throw before the bound fires that are not among the transport failures named so
    /// far, each with a message naming the server, what it said and the recipient.
    /// </summary>
    private static readonly Dictionary<string, Func<string, Exception>> UnlistedFailures = new()
    {
        ["a timeout"] = text => new TimeoutException(text),
        ["a SASL failure"] = text => new SaslException("PLAIN", SaslErrorCode.InvalidChallenge, text),
        ["MailKit's authentication failure"] = text => new MailKit.Security.AuthenticationException(text),
        ["the platform's authentication failure"] = text => new System.Security.Authentication.AuthenticationException(text),
        ["an unsupported operation"] = text => new NotSupportedException(text),
        ["an invalid operation"] = text => new InvalidOperationException(text),
    };

    [Fact]
    public async Task A_client_that_is_not_authenticated_fails_as_not_sent_without_the_servers_text()
    {
        var client = Substitute.For<ISmtpClient>();
        client.ConnectAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<SecureSocketOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ServiceNotAuthenticatedException(ServerText + " " + Recipient));
        var sender = Sender(client, new FakeTimeProvider());

        var failed = await Should.ThrowAsync<MailNotSentException>(() => sender.SendAsync(Invitation, TestContext.Current.CancellationToken));

        ShouldNameNeitherTheServersTextNorTheRecipient(failed);
    }

    [Fact]
    public async Task A_client_that_is_not_connected_fails_as_not_sent_without_the_servers_text()
    {
        var client = Substitute.For<ISmtpClient>();
        client.ConnectAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<SecureSocketOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new ServiceNotConnectedException(ServerText + " " + Recipient));
        var sender = Sender(client, new FakeTimeProvider());

        var failed = await Should.ThrowAsync<MailNotSentException>(() => sender.SendAsync(Invitation, TestContext.Current.CancellationToken));

        ShouldNameNeitherTheServersTextNorTheRecipient(failed);
    }

    [Fact]
    public async Task A_transport_failure_after_the_bound_fired_surfaces_as_the_bounds_cancellation_without_the_servers_text()
    {
        // MailKit may answer the bound's cancellation with an IOException instead of an OperationCanceledException.
        var time = new FakeTimeProvider();
        var client = Substitute.For<ISmtpClient>();
        client.ConnectAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<SecureSocketOptions>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                time.Advance(TimeSpan.FromSeconds(11));
                return Task.FromException(new IOException(ServerText + " " + Recipient));
            });
        var sender = Sender(client, time);

        var failed = await Should.ThrowAsync<OperationCanceledException>(() => sender.SendAsync(Invitation, TestContext.Current.CancellationToken));

        failed.Message.ShouldNotContain(ServerText, Case.Insensitive);
        failed.Message.ShouldNotContain(Recipient, Case.Insensitive);
        failed.InnerException.ShouldBeNull();
    }

    [Fact]
    public async Task A_transport_failure_before_the_bound_fired_is_not_sent_and_no_cancellation()
    {
        var client = Substitute.For<ISmtpClient>();
        client.ConnectAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<SecureSocketOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException(ServerText + " " + Recipient));
        var sender = Sender(client, new FakeTimeProvider());

        var failed = await Should.ThrowAsync<MailNotSentException>(() => sender.SendAsync(Invitation, TestContext.Current.CancellationToken));

        ShouldNameNeitherTheServersTextNorTheRecipient(failed);
    }

    [Theory]
    [InlineData("a timeout")]
    [InlineData("a SASL failure")]
    [InlineData("MailKit's authentication failure")]
    [InlineData("the platform's authentication failure")]
    [InlineData("an unsupported operation")]
    [InlineData("an invalid operation")]
    public async Task Any_failure_of_the_client_before_the_bound_fired_is_not_sent_without_the_servers_address_or_text(string failure)
    {
        var client = Substitute.For<ISmtpClient>();
        client.ConnectAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<SecureSocketOptions>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(UnlistedFailures[failure]($"{ServerAddress} answered {ServerText} for {Recipient}"));
        var sender = Sender(client, new FakeTimeProvider());

        var failed = await Should.ThrowAsync<MailNotSentException>(() => sender.SendAsync(Invitation, TestContext.Current.CancellationToken));

        failed.Message.ShouldNotContain(ServerAddress, Case.Insensitive);
        failed.Message.ShouldNotContain("smtp.example.net", Case.Insensitive);
        ShouldNameNeitherTheServersTextNorTheRecipient(failed);
    }

    [Theory]
    [InlineData("a timeout")]
    [InlineData("a SASL failure")]
    [InlineData("an unsupported operation")]
    [InlineData("an invalid operation")]
    public async Task Any_failure_of_the_client_while_sending_is_not_sent_without_the_servers_address_or_text(string failure)
    {
        var client = Substitute.For<ISmtpClient>();
        client.SendAsync(Arg.Any<MimeKit.MimeMessage>(), Arg.Any<CancellationToken>(), Arg.Any<ITransferProgress>())
            .ThrowsAsync(UnlistedFailures[failure]($"{ServerAddress} answered {ServerText} for {Recipient}"));
        var sender = Sender(client, new FakeTimeProvider());

        var failed = await Should.ThrowAsync<MailNotSentException>(() => sender.SendAsync(Invitation, TestContext.Current.CancellationToken));

        failed.Message.ShouldNotContain(ServerAddress, Case.Insensitive);
        ShouldNameNeitherTheServersTextNorTheRecipient(failed);
    }

    /// <summary>
    /// Once the client's <c>SendAsync</c> has returned, the server has taken the mail: a failure while disconnecting
    /// afterwards counts as sent, so the send completes and nothing retries a mail the invitee already has.
    /// </summary>
    [Theory]
    [InlineData("a dropped connection")]
    [InlineData("a refused QUIT")]
    [InlineData("a timeout")]
    public async Task A_failure_while_disconnecting_after_the_mail_was_sent_counts_as_sent(string failure)
    {
        var client = Substitute.For<ISmtpClient>();
        client.DisconnectAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).ThrowsAsync(DisconnectFailure(failure));
        var sender = Sender(client, new FakeTimeProvider());

        // Awaited directly, not through Should.NotThrowAsync, which lets a cancelled task pass.
        await sender.SendAsync(Invitation, TestContext.Current.CancellationToken);

        await client.Received(1).SendAsync(Arg.Any<MimeKit.MimeMessage>(), Arg.Any<CancellationToken>(), Arg.Any<ITransferProgress>());
    }

    /// <summary>The bound that fires while disconnecting, after the mail was sent, does not undo the send either.</summary>
    [Fact]
    public async Task The_bound_firing_while_disconnecting_after_the_mail_was_sent_counts_as_sent()
    {
        var time = new FakeTimeProvider();
        var client = Substitute.For<ISmtpClient>();
        client.DisconnectAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(_ =>
        {
            time.Advance(TimeSpan.FromSeconds(11));
            return Task.FromException(new IOException(ServerText + " " + Recipient));
        });
        var sender = Sender(client, time);

        // Awaited directly, not through Should.NotThrowAsync, which lets a cancelled task pass.
        await sender.SendAsync(Invitation, TestContext.Current.CancellationToken);
    }

    private static Exception DisconnectFailure(string failure) => failure switch
    {
        "a dropped connection" => new IOException($"{ServerAddress} answered {ServerText} for {Recipient}"),
        "a refused QUIT" => new SmtpCommandException(SmtpErrorCode.UnexpectedStatusCode, SmtpStatusCode.ServiceNotAvailable, $"{ServerText} for {Recipient}"),
        "a timeout" => new TimeoutException($"{ServerAddress} answered {ServerText} for {Recipient}"),
        _ => throw new InvalidOperationException($"No failure named '{failure}'."),
    };

    private static SmtpMailSender Sender(ISmtpClient client, TimeProvider time) =>
        new(Settings.Of((ServerKey, "smtp://127.0.0.1:25"), (FromKey, "noreply@example.com")), time, () => client);

    private static void ShouldNameNeitherTheServersTextNorTheRecipient(MailNotSentException failed)
    {
        failed.Message.ShouldNotContain(ServerText, Case.Insensitive);
        failed.Message.ShouldNotContain(Recipient, Case.Insensitive);
        failed.InnerException.ShouldBeNull();
    }
}
