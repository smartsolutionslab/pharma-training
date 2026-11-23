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

    /// <summary>
    /// Whether the server may hold the mail after all: <see langword="false"/> when the failure came before the mail was
    /// handed over (a missing setting, connect, TLS, login) or the server refused it at any step, so the recipient cannot
    /// have it; <see langword="true"/> for a transport, protocol or timeout failure once the send started. One that does
    /// not say is <see langword="true"/>, the side that keeps what the mail carries.
    /// </summary>
    public bool MayHaveBeenDelivered { get; } = true;
}
