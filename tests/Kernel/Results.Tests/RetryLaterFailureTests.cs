using System.Net;

namespace SmartSolutionsLab.PharmaTraining.Kernel.Tests;

/// <summary>
/// A domain rule that refuses for now, not for good, is mapped with <see cref="ApiError.RetryLater"/>: 429 with the
/// domain error's code and message, and the instant from which the caller may try again.
/// </summary>
public class RetryLaterFailureTests
{
    private static readonly DateTimeOffset RetryAt = new(2027, 4, 1, 8, 10, 0, TimeSpan.Zero);

    [Fact]
    public void RetryLater_answers_429_with_the_domain_errors_code_message_and_instant()
    {
        var refused = new RenewedTooSoon();

        var error = ApiError.RetryLater(refused, RetryAt);

        error.Code.ShouldBe("billing.renewed-too-soon");
        error.Message.ShouldBe("Renewed a moment ago.");
        error.Status.ShouldBe(HttpStatusCode.TooManyRequests);
        var later = error.ShouldBeOfType<RetryLaterFailure>();
        later.Error.ShouldBe(refused);
        later.RetryAt.ShouldBe(RetryAt);
    }

    [Fact]
    public void RetryLater_refuses_a_missing_error() =>
        Should.Throw<GuardException>(() => ApiError.RetryLater(null!, RetryAt));

    private sealed record RenewedTooSoon() : DomainError("billing.renewed-too-soon", "Renewed a moment ago.");
}
