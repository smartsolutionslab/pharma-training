namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;

/// <summary>
/// The SMTP server refused the mail. Its message names the step and the codes only, and it keeps no inner exception:
/// the server's own text often echoes the recipient, and the exception reaches logs and dead letters.
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
