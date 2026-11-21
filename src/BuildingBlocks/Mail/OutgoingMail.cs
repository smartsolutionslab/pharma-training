namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;

/// <summary>A plain-text mail to one address. Plain text only: nothing a request carries becomes markup.</summary>
public sealed record OutgoingMail
{
    /// <summary>The longest address SMTP carries (RFC 5321).</summary>
    public const int MaxAddressLength = 254;

    public OutgoingMail(string to, string subject, string text)
    {
        Ensure.That(to, nameof(to)).IsNotNullOrWhiteSpace().HasMaxLength(MaxAddressLength);
        Ensure.That(subject, nameof(subject)).IsNotNullOrWhiteSpace();
        Ensure.That(text, nameof(text)).IsNotNullOrWhiteSpace();
        To = to;
        Subject = subject;
        Text = text;
    }

    public string To { get; }

    public string Subject { get; }

    public string Text { get; }
}
