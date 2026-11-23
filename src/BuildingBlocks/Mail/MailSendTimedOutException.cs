namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;

/// <summary>
/// The send's bound fired: the bound's own cancellation, with no inner exception and no server text, which says whether
/// the mail was already being handed to the server when it fired.
/// </summary>
public sealed class MailSendTimedOutException : OperationCanceledException
{
    public MailSendTimedOutException()
    {
    }

    public MailSendTimedOutException(string message)
        : base(message)
    {
    }

    public MailSendTimedOutException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <param name="mayHaveBeenDelivered">Whether it fired once the mail was being handed to the server.</param>
    /// <param name="bound">The bound's token, which fired.</param>
    public MailSendTimedOutException(bool mayHaveBeenDelivered, CancellationToken bound)
        : base(bound) => MayHaveBeenDelivered = mayHaveBeenDelivered;

    /// <summary>
    /// Whether the server may hold the mail after all: <see langword="false"/> only when the bound fired before the mail
    /// was handed over. One that does not say is <see langword="true"/>.
    /// </summary>
    public bool MayHaveBeenDelivered { get; } = true;
}
