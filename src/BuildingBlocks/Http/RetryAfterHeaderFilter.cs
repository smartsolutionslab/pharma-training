using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Http;

/// <summary>
/// Sets <c>Retry-After</c>, in whole seconds rounded up and at least one, on a 429 problem that says when it may be
/// retried (<see cref="ResultHttpExtensions.RetryAtExtension"/>, from <see cref="ApiError.RetryLater"/>). Every
/// module's route group carries it, so no endpoint sets the header itself.
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
            var seconds = Math.Max(1, (long)Math.Ceiling((retryAt - time.GetUtcNow()).TotalSeconds));
            context.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        }

        return result;
    }
}
