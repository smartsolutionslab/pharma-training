using MimeKit;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail.Tests;

/// <summary>The message is plain text with its headers set through MimeKit, so nothing in a subject becomes a header.</summary>
public class SmtpMailSenderTests
{
    private static readonly MailboxAddress From = MailboxAddress.Parse("noreply@example.com");

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
}
