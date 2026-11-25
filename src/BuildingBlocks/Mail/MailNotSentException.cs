namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;

/// <summary>
/// The SMTP server refused the mail, the connection, TLS, login or protocol failed, or a setting the send needs is
/// missing or unusable. Its message names the step and
/// the codes, or the kind of failure, only, and it keeps no inner exception: the server's own text often echoes the
/// recipient, and the exception reaches logs and dead letters.
/// </summary>
public sealed class MailNotSentException : InvalidOperationException
{
    public MailNotSentException()
    {
    }

    public MailNotSentException(string message)
        : base(message)
    {
    }

    public MailNotSentException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <param name="message">The step and the codes, or the kind of failure, only.</param>
    /// <param name="mayHaveBeenDelivered">Whether the failure came once the mail was being handed to the server.</param>
    public MailNotSentException(string message, bool mayHaveBeenDelivered)
        : base(message) => MayHaveBeenDelivered = mayHaveBeenDelivered;

    /// <param name="message">The step and the codes, or the kind of failure, only.</param>
    /// <param name="mayHaveBeenDelivered">Whether the failure came once the mail was being handed to the server.</param>
    /// <param name="recipientRefused">Whether the server refused the recipient for good (see <see cref="RecipientRefused"/>).</param>
    public MailNotSentException(string message, bool mayHaveBeenDelivered, bool recipientRefused)
        : base(message)
    {
        MayHaveBeenDelivered = mayHaveBeenDelivered;
        RecipientRefused = recipientRefused;
    }

    /// <summary>
    /// Whether the server may hold the mail after all: <see langword="false"/> when the failure came before the mail was
    /// handed over (a missing setting, connect, TLS, login) or the server refused it at any step, so the recipient cannot
    /// have it; <see langword="true"/> for a transport, protocol or timeout failure once the send started, and for one
    /// made without saying. A caller counts a mail as possibly delivered only when a sender's failure is this exception
    /// or a <see cref="MailSendTimedOutException"/> with this set to <see langword="true"/>; every other exception from
    /// an <see cref="IMailSender"/> counts as not delivered. So a sender that cannot rule out delivery must throw
    /// <c>new MailNotSentException(message, true)</c>, never let another exception through.
    /// </summary>
    public bool MayHaveBeenDelivered { get; } = true;

    /// <summary>
    /// Whether the server refused the recipient's address for good (a 5xx reply to RCPT TO, such as 550 5.1.1), so
    /// sending the same mail again cannot reach it; <see langword="false"/> for every other failure, a temporary (4xx)
    /// refusal of the recipient and a refusal of the sender or the message included.
    /// </summary>
    public bool RecipientRefused { get; }
}
