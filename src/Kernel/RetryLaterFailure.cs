using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Kernel;

/// <summary>
/// An <see cref="ApiError"/> made from a <see cref="DomainError"/> by <see cref="ApiError.RetryLater"/>: a refusal for
/// now, not for good, answered with 429 and the instant from which the caller may try again.
/// </summary>
public sealed record RetryLaterFailure : ApiError
{
    internal RetryLaterFailure(DomainError error, DateTimeOffset retryAt)
        : base(error.Code, error.Message, HttpStatusCode.TooManyRequests)
    {
        Error = error;
        RetryAt = retryAt;
    }

    public DomainError Error { get; }

    /// <summary>The instant from which the refused request may be tried again.</summary>
    public DateTimeOffset RetryAt { get; }
}
