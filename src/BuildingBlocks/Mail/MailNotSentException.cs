namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;

/// <summary>Red-first stub (T4): the SMTP server refused the mail. Nothing throws it yet.</summary>
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
