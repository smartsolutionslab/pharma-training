namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;

/// <summary>
/// The SMTP server refused the mail, or the connection, TLS, login or protocol failed. Its message names the step and
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
}
