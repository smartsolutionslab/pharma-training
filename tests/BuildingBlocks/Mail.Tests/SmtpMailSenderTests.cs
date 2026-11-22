using Microsoft.Extensions.Time.Testing;
using MimeKit;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail.Tests;

/// <summary>The message is plain text with its headers set through MimeKit, so nothing in a subject becomes a header.</summary>
public class SmtpMailSenderTests
{
    private const string ServerKey = "ConnectionStrings:mail";

    private const string FromKey = "Mail:From";

    private static readonly MailboxAddress From = MailboxAddress.Parse("noreply@example.com");

    private const string Recipient = "anna@example.org";

    /// <summary>Text only the server writes, so a message that holds it carries what the server said.</summary>
    private const string ServerText = "only-the-server-wrote-this";

    private static readonly OutgoingMail Invitation = new(Recipient, "Einladung", "Text");

    [Fact]
    public void The_message_has_its_sender_recipient_subject_and_one_plain_text_part()
    {
        var mail = new OutgoingMail("anna@example.org", "Einladung zu Apotheke am Markt", "Guten Tag,\nein Link: https://app.example/x");

        var message = SmtpMailSender.Compose(mail, From);

        message.From.Mailboxes.ShouldHaveSingleItem().Address.ShouldBe("noreply@example.com");
        message.To.Mailboxes.ShouldHaveSingleItem().Address.ShouldBe("anna@example.org");
        message.Subject.ShouldBe("Einladung zu Apotheke am Markt");
        var body = message.Body.ShouldBeOfType<TextPart>();
        body.IsPlain.ShouldBeTrue();
        body.Text.ShouldBe(mail.Text);
    }

    [Fact]
    public void A_subject_that_carries_a_header_adds_no_header()
    {
        var message = SmtpMailSender.Compose(new OutgoingMail("anna@example.org", "Einladung\r\nBcc: x@example.org", "Text"), From);

        message.Bcc.ShouldBeEmpty();
        message.Headers.Contains(HeaderId.Bcc).ShouldBeFalse();
        using var written = new MemoryStream();
        message.WriteTo(written, TestContext.Current.CancellationToken);
        var lines = System.Text.Encoding.UTF8.GetString(written.ToArray()).Split("\r\n");
        lines.ShouldNotContain(line => line.StartsWith("Bcc:", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_recipient_the_server_refuses_fails_with_its_codes_and_without_the_address()
    {
        await using var server = new SmtpTestServer(refuse: ("RCPT", "550 5.1.1 <{0}>: Recipient address rejected"));
        var sender = new SmtpMailSender(Settings.Of((ServerKey, server.ConnectionString), (FromKey, "noreply@example.com")));

        var refused = await Should.ThrowAsync<MailNotSentException>(() => sender.SendAsync(Invitation, TestContext.Current.CancellationToken));

        refused.Message.ShouldContain("550", Case.Sensitive);
        refused.Message.ShouldNotContain("anna@example.org", Case.Insensitive);
        refused.InnerException.ShouldBeNull();
    }

    [Fact]
    public async Task A_reply_the_client_cannot_read_fails_as_not_sent_without_the_servers_text_or_the_address()
    {
        // No status code: MailKit throws SmtpProtocolException, not SmtpCommandException.
        await using var server = new SmtpTestServer(refuse: ("RCPT", "XYZ <{0}>: " + ServerText));
        var sender = new SmtpMailSender(Settings.Of((ServerKey, server.ConnectionString), (FromKey, "noreply@example.com")));

        var failed = await Should.ThrowAsync<MailNotSentException>(() => sender.SendAsync(Invitation, TestContext.Current.CancellationToken));

        ShouldNameNeitherTheServersTextNorTheRecipient(failed);
    }

    [Fact]
    public async Task A_connection_the_server_resets_fails_as_not_sent_without_the_address()
    {
        // The reset surfaces as an IOException from the socket's read.
        await using var server = SmtpTestServer.Resetting("RCPT");
        var sender = new SmtpMailSender(Settings.Of((ServerKey, server.ConnectionString), (FromKey, "noreply@example.com")));

        var failed = await Should.ThrowAsync<MailNotSentException>(() => sender.SendAsync(Invitation, TestContext.Current.CancellationToken));

        ShouldNameNeitherTheServersTextNorTheRecipient(failed);
    }

    [Fact]
    public async Task A_tls_handshake_that_fails_fails_as_not_sent_without_the_servers_text_or_the_address()
    {
        // smtps to a server that speaks plain SMTP: MailKit throws SslHandshakeException.
        await using var server = new SmtpTestServer();
        var tls = server.ConnectionString.Replace("smtp://", "smtps://", StringComparison.Ordinal);
        var sender = new SmtpMailSender(Settings.Of((ServerKey, tls), (FromKey, "noreply@example.com")));

        var failed = await Should.ThrowAsync<MailNotSentException>(() => sender.SendAsync(Invitation, TestContext.Current.CancellationToken));

        ShouldNameNeitherTheServersTextNorTheRecipient(failed);
        failed.Message.ShouldNotContain("test ESMTP", Case.Insensitive);
    }

    [Fact]
    public async Task Without_a_sender_address_sending_fails_naming_the_setting()
    {
        var sender = new SmtpMailSender(Settings.Of(("ConnectionStrings:mail", "smtp://localhost:1"), ("Mail:From", null)));

        var refused = await Should.ThrowAsync<InvalidOperationException>(
            () => sender.SendAsync(new OutgoingMail("anna@example.org", "Betreff", "Text"), TestContext.Current.CancellationToken));

        refused.Message.ShouldContain("Mail:From");
    }

    [Theory]
    [InlineData("\"evil\"<a@b.c>")]
    [InlineData("a(b)@example.com")]
    [InlineData("Anna <anna@example.org>")]
    public void The_recipient_is_the_mails_address_as_it_is_never_one_parsed_out_of_it(string to)
    {
        var mail = new OutgoingMail(to, "Betreff", "Text");
        MimeMessage message;
        try
        {
            message = SmtpMailSender.Compose(mail, From);
        }
        catch (Exception)
        {
            // Refusing an address that would reach another mailbox is as good as sending to it as it is.
            return;
        }

        message.To.Mailboxes.ShouldHaveSingleItem().Address.ShouldBe(to);
    }

    [Fact]
    public async Task The_whole_send_is_bounded_at_ten_seconds_although_no_step_takes_ten()
    {
        await using var server = new SmtpTestServer("EHLO", "MAIL", "RCPT");
        var time = new FakeTimeProvider();
        var sender = new SmtpMailSender(Settings.Of((ServerKey, server.ConnectionString), (FromKey, "noreply@example.com")), time);
        var cancellationToken = TestContext.Current.CancellationToken;

        var sending = sender.SendAsync(Invitation, CancellationToken.None);
        await server.ReceivedAsync("EHLO").WaitAsync(cancellationToken);
        time.Advance(TimeSpan.FromSeconds(4));
        server.Release("EHLO");
        await server.ReceivedAsync("MAIL").WaitAsync(cancellationToken);
        time.Advance(TimeSpan.FromSeconds(4));
        server.Release("MAIL");
        await server.ReceivedAsync("RCPT").WaitAsync(cancellationToken);
        time.Advance(TimeSpan.FromSeconds(3));

        var first = await Task.WhenAny(sending, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken));
        first.ShouldBe(sending, "The send still waits after 11 s of steps of 4, 4 and 3 s.");
        sending.IsCompletedSuccessfully.ShouldBeFalse();
        server.Messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_send_that_has_started_is_not_cancelled_by_the_callers_token()
    {
        await using var server = new SmtpTestServer("EHLO");
        var sender = new SmtpMailSender(Settings.Of((ServerKey, server.ConnectionString), (FromKey, "noreply@example.com")), new FakeTimeProvider());
        var cancellationToken = TestContext.Current.CancellationToken;
        using var caller = new CancellationTokenSource();

        var sending = sender.SendAsync(Invitation, caller.Token);
        await server.ReceivedAsync("EHLO").WaitAsync(cancellationToken);
        await caller.CancelAsync();
        await Task.WhenAny(sending, Task.Delay(TimeSpan.FromSeconds(1), cancellationToken));
        server.Release("EHLO");

        await sending.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ContinueOnCapturedContext);
        sending.Status.ShouldBe(TaskStatus.RanToCompletion, "The caller's token cancelled a send that had started.");
        server.Recipients.ShouldBe(["anna@example.org"]);
        server.Messages.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_sender_address_supplied_after_a_failed_send_is_used_by_the_next_send()
    {
        await using var server = new SmtpTestServer();
        var configuration = Settings.Of((ServerKey, server.ConnectionString));
        var sender = new SmtpMailSender(configuration);
        var cancellationToken = TestContext.Current.CancellationToken;
        await Should.ThrowAsync<InvalidOperationException>(() => sender.SendAsync(Invitation, cancellationToken));

        configuration[FromKey] = "noreply@example.com";

        await Should.NotThrowAsync(() => sender.SendAsync(Invitation, cancellationToken));
        server.Messages.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task A_server_supplied_after_a_failed_send_is_used_by_the_next_send()
    {
        await using var server = new SmtpTestServer();
        var configuration = Settings.Of((FromKey, "noreply@example.com"));
        var sender = new SmtpMailSender(configuration);
        var cancellationToken = TestContext.Current.CancellationToken;
        await Should.ThrowAsync<InvalidOperationException>(() => sender.SendAsync(Invitation, cancellationToken));

        configuration[ServerKey] = server.ConnectionString;

        await Should.NotThrowAsync(() => sender.SendAsync(Invitation, cancellationToken));
        server.Messages.ShouldHaveSingleItem();
    }

    private static void ShouldNameNeitherTheServersTextNorTheRecipient(MailNotSentException failed)
    {
        failed.Message.ShouldNotContain(ServerText, Case.Insensitive);
        failed.Message.ShouldNotContain(Recipient, Case.Insensitive);
        failed.InnerException.ShouldBeNull();
    }
}
