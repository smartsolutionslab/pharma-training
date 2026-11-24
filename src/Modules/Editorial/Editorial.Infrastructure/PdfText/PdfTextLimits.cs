namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Infrastructure.PdfText;

/// <summary>
/// The bounds on reading PDF text in one host (#316, ADR 0008 D11): how many parses run at once, from
/// <see cref="FewestParses"/> to <see cref="MostParses"/>, and how long one may run before it is answered as no text,
/// from <see cref="ShortestTimeoutSeconds"/> to <see cref="LongestTimeoutSeconds"/> seconds.
/// </summary>
public sealed record PdfTextLimits
{
    public const int FewestParses = 1;

    public const int MostParses = 16;

    public const int DefaultParses = 2;

    public const int ShortestTimeoutSeconds = 1;

    public const int LongestTimeoutSeconds = 300;

    public const int DefaultTimeoutSeconds = 30;

    private PdfTextLimits(int maxConcurrentParses, TimeSpan timeout)
    {
        MaxConcurrentParses = maxConcurrentParses;
        Timeout = timeout;
    }

    /// <summary>The defaults: two parses at once, 30 seconds each.</summary>
    public static PdfTextLimits Default { get; } = new(DefaultParses, TimeSpan.FromSeconds(DefaultTimeoutSeconds));

    public int MaxConcurrentParses { get; }

    public TimeSpan Timeout { get; }

    public static PdfTextLimits Of(int maxConcurrentParses, TimeSpan timeout)
    {
        Ensure.That(maxConcurrentParses).InRange(FewestParses, MostParses);
        Ensure.That((decimal)timeout.TotalSeconds, nameof(timeout)).InRange(ShortestTimeoutSeconds, LongestTimeoutSeconds);
        return new PdfTextLimits(maxConcurrentParses, timeout);
    }
}
