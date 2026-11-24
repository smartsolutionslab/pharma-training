using Marten.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Http;

namespace Microsoft.Extensions.DependencyInjection;

public static partial class DuplicateStreamResponses
{
    /// <summary>
    /// A save that starts a stream another save has already started answers <c>409</c> problem details with the code
    /// of <see cref="AlreadyExists"/>; every other event store failure stays a <c>500</c>.
    /// </summary>
    public static IServiceCollection AddDuplicateStreamResponses(this IServiceCollection services) =>
        services.AddProblemDetails()
            .AddExceptionHandler<DuplicateStreamExceptionHandler>();

    /// <summary>
    /// The backstop for two creates of one aggregate id, the event store's counterpart of a unique index. Both can find
    /// no aggregate before either saves; Marten then refuses the second stream (<see cref="ExistingStreamIdCollisionException"/>,
    /// Marten's own, not JasperFx's of the same name), and this answers <c>409</c> instead of <c>500</c>.
    /// </summary>
    private sealed partial class DuplicateStreamExceptionHandler(IProblemDetailsService problemDetails, ILogger<DuplicateStreamExceptionHandler> logger) : IExceptionHandler
    {
        /// <summary>The code, title and status of every problem a duplicate stream answers, the one a unique index's refusal answers too.</summary>
        private static readonly AlreadyExists Refusal = new();

        /// <summary>
        /// Only a stream that had to be new: an append that loses to another on a stream that exists
        /// (<see cref="JasperFx.Events.EventStreamUnexpectedMaxEventIdException"/>) is a lost update, not a duplicate, and stays a <c>500</c>.
        /// </summary>
        public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            Ensure.That(httpContext).IsNotNull();

            if (exception is not ExistingStreamIdCollisionException)
            {
                return false;
            }

            LogRefused(logger);

            httpContext.Response.StatusCode = (int)Refusal.Status;

            // Nothing from the exception reaches the caller: its message names the stream.
            await problemDetails.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = httpContext,
                ProblemDetails =
                {
                    Status = (int)Refusal.Status,
                    Title = Refusal.Message,
                    Detail = "Choose a different one; retrying this request unchanged is refused again.",
                    Extensions = { ["code"] = Refusal.Code },
                },
            });

            return true;
        }

        [LoggerMessage(Level = LogLevel.Information, Message = "The event store refused a stream that already exists; answered 409")]
        private static partial void LogRefused(ILogger logger);
    }
}
