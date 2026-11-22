using Microsoft.Extensions.Time.Testing;
using MimeKit;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail.Tests;

/// <summary>The message is plain text with its headers set through MimeKit, so nothing in a subject becomes a header.</summary>
public class SmtpMailSenderTests
{
    private const string ServerKey = "ConnectionStrings:mail";

    private const string FromKey = "Mail:From";

    private static readonly MailboxAddress From = MailboxAddress.Parse("noreply@example.com");

    private static readonly OutgoingMail Invitation = new("anna@example.org", "Einladung", "Text");

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
}
