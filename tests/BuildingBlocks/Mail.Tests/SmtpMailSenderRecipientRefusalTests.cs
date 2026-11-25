using MailKit;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Time.Testing;
using MimeKit;
using NSubstitute.ExceptionExtensions;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail.Tests;

/// <summary>
/// A server that refuses the recipient for good says so on <see cref="MailNotSentException.RecipientRefused"/>, so a caller
/// can give that address up instead of retrying it (#115): only a 550, 551 or 553 reply to RCPT TO whose enhanced status
/// code is 5.1.x, a bad mailbox. A temporary (4xx) refusal of the recipient, a refusal that is the relay's or the
/// account's (530, 5.7.x), a reply without an enhanced code, and a refusal of the sender or the message do not. None of them may have been delivered, and none carries the server's text
/// or the recipient.
/// </summary>
public class SmtpMailSenderRecipientRefusalTests
{
    private const string ServerKey = "ConnectionStrings:mail";

    private const string FromKey = "Mail:From";

    private const string Recipient = "anna@example.org";

    private const string ServerText = "only-the-server-wrote-this";

    private static readonly OutgoingMail Mail = new(Recipient, "Betreff", "Text");

    [Theory]
    [InlineData(SmtpStatusCode.MailboxUnavailable)]
    [InlineData(SmtpStatusCode.UserNotLocalTryAlternatePath)]
    [InlineData(SmtpStatusCode.MailboxNameNotAllowed)]
    public async Task A_recipient_the_server_refuses_for_good_is_a_refused_recipient(SmtpStatusCode status)
    {
        var failed = await RefusedAsync(SmtpErrorCode.RecipientNotAccepted, status, "5.1.1");

        failed.RecipientRefused.ShouldBeTrue();
        failed.MayHaveBeenDelivered.ShouldBeFalse();
        ShouldNameNeitherTheServersTextNorTheRecipient(failed);
    }

    [Theory]
    [InlineData(SmtpStatusCode.MailboxBusy)]
    [InlineData(SmtpStatusCode.ErrorInProcessing)]
    [InlineData(SmtpStatusCode.InsufficientStorage)]
    public async Task A_recipient_the_server_refuses_for_now_is_no_refused_recipient(SmtpStatusCode status)
    {
        var failed = await RefusedAsync(SmtpErrorCode.RecipientNotAccepted, status);

        failed.RecipientRefused.ShouldBeFalse();
        failed.MayHaveBeenDelivered.ShouldBeFalse();
        ShouldNameNeitherTheServersTextNorTheRecipient(failed);
    }

    [Theory]
    [InlineData(SmtpStatusCode.AuthenticationRequired, "5.7.0")]
    [InlineData(SmtpStatusCode.TransactionFailed, "5.7.1")]
    [InlineData(SmtpStatusCode.MailboxUnavailable, "5.7.1")]
    [InlineData(SmtpStatusCode.MailboxUnavailable, "")]
    [InlineData(SmtpStatusCode.MailboxNameNotAllowed, "5.4.1")]
    public async Task A_recipient_refused_by_the_relay_or_without_a_bad_mailbox_code_is_no_refused_recipient(SmtpStatusCode status, string enhanced)
    {
        // A misconfigured relay (530 authentication required, 5.7.x relay denied) answers every recipient so; giving each
        // up would erase a person without mailing them, so such a refusal is retried like any other failure.
        var failed = await RefusedAsync(SmtpErrorCode.RecipientNotAccepted, status, enhanced);

        failed.RecipientRefused.ShouldBeFalse();
        failed.MayHaveBeenDelivered.ShouldBeFalse();
        ShouldNameNeitherTheServersTextNorTheRecipient(failed);
    }

    [Theory]
    [InlineData(SmtpErrorCode.SenderNotAccepted, SmtpStatusCode.MailboxUnavailable)]
    [InlineData(SmtpErrorCode.MessageNotAccepted, SmtpStatusCode.TransactionFailed)]
    [InlineData(SmtpErrorCode.UnexpectedStatusCode, SmtpStatusCode.MailboxUnavailable)]
    public async Task A_refusal_of_anything_but_the_recipient_is_no_refused_recipient(SmtpErrorCode step, SmtpStatusCode status)
    {
        var failed = await RefusedAsync(step, status);

        failed.RecipientRefused.ShouldBeFalse();
        failed.MayHaveBeenDelivered.ShouldBeFalse();
        ShouldNameNeitherTheServersTextNorTheRecipient(failed);
    }

    private static async Task<MailNotSentException> RefusedAsync(SmtpErrorCode step, SmtpStatusCode status, string enhanced = "5.1.1")
    {
        var text = $"{enhanced} {ServerText} {Recipient}".TrimStart();
        var refusal = step == SmtpErrorCode.RecipientNotAccepted
            ? new SmtpCommandException(step, status, MailboxAddress.Parse(Recipient), text)
            : new SmtpCommandException(step, status, text);
        var client = Substitute.For<ISmtpClient>();
        client.SendAsync(Arg.Any<MimeMessage>(), Arg.Any<CancellationToken>(), Arg.Any<ITransferProgress>()).ThrowsAsync(refusal);
        var sender = new SmtpMailSender(Settings.Of((ServerKey, "smtp://127.0.0.1:25"), (FromKey, "noreply@example.com")), new FakeTimeProvider(), () => client);

        return await Should.ThrowAsync<MailNotSentException>(() => sender.SendAsync(Mail, TestContext.Current.CancellationToken));
    }

    private static void ShouldNameNeitherTheServersTextNorTheRecipient(MailNotSentException failed)
    {
        failed.Message.ShouldNotContain(ServerText, Case.Insensitive);
        failed.Message.ShouldNotContain(Recipient, Case.Insensitive);
        failed.InnerException.ShouldBeNull();
    }
}
