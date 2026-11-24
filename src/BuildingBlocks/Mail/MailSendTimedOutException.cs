namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;

/// <summary>
/// The send's bound fired, or the caller had cancelled before the send began: a cancellation with no inner exception and
/// no server text, which says whether the mail was already being handed to the server when it came.
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
    /// Whether the server may hold the mail after all: <see langword="false"/> only when the bound fired, or the caller
    /// had cancelled, before the mail was handed over; one made without saying is <see langword="true"/>. Only
    /// <see langword="true"/> counts as possibly delivered: a plain <see cref="OperationCanceledException"/>, like every
    /// exception from an <see cref="IMailSender"/> other than this one or a <see cref="MailNotSentException"/> with
    /// <c>MayHaveBeenDelivered</c> set, counts as not delivered. A sender that cannot rule out delivery must throw
    /// <c>new MailNotSentException(message, true)</c>, or this exception with <see langword="true"/> when its own bound
    /// fired.
    /// </summary>
    public bool MayHaveBeenDelivered { get; } = true;
}
