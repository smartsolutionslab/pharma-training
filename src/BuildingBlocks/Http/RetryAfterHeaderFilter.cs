using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Http;

/// <summary>
/// Sets <c>Retry-After</c>, in whole seconds rounded up and at least one, on a 429 problem that says when it may be
/// retried (<see cref="ResultHttpExtensions.RetryAtExtension"/>, from <see cref="ApiError.RetryLater"/>). The wait is
/// counted from the instant the refusal was refused at (<see cref="ResultHttpExtensions.RefusedAtExtension"/>) when it
/// carries one, which is on the clock the retry instant was computed on, so a host whose clock is off answers the same
/// wait; otherwise from the host's <see cref="TimeProvider"/>. Every module's route group carries it, so no endpoint
/// sets the header itself.
/// </summary>
public sealed class RetryAfterHeaderFilter(TimeProvider time) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        Ensure.That(context).IsNotNull();
        Ensure.That(next).IsNotNull();

        var result = await next(context);
        var answered = result is INestedHttpResult nested ? nested.Result : result;
        if (answered is ProblemHttpResult { StatusCode: StatusCodes.Status429TooManyRequests } problem
            && problem.ProblemDetails.Extensions.TryGetValue(ResultHttpExtensions.RetryAtExtension, out var value)
            && value is DateTimeOffset retryAt)
        {
            var from = problem.ProblemDetails.Extensions.TryGetValue(ResultHttpExtensions.RefusedAtExtension, out var refused)
                && refused is DateTimeOffset refusedAt
                    ? refusedAt
                    : time.GetUtcNow();
            var seconds = Math.Max(1, (long)Math.Ceiling((retryAt - from).TotalSeconds));
            context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        }

        return result;
    }
}
