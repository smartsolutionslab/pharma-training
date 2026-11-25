using System.Security.Claims;
using System.Text.Json;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.PublishCourseVersion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RetireCourseVersion;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ReturnToDraft;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.SubmitForReview;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>
/// The editorial workflow: an editor submits a draft for review, returns it to draft, publishes it and retires it.
/// Each step is a POST on the version, taken by an editor whose token names a person, and records the caller. Submit,
/// publish and retire take no body. A return also records the caller's display name, so a caller whose token names no
/// usable one is refused (403 <c>editorial.reviewer-name-missing</c>) after the caller's person and before the body is
/// read; it takes its reason (<see cref="ReturnEditorialCourseVersionRequest"/>), read after authorization, the
/// caller's person and name, whatever its content type: a reason that is missing or invalid, and a
/// body that cannot be read (not JSON, not UTF-8), answer 400 <c>editorial.invalid-return-reason</c> with one message,
/// and a body larger than <see cref="ReturnRequestLimit"/> is refused (413). The empty Guid is no version. The reason is
/// checked before the version is loaded, so a bad reason answers 400 even for an unknown version (rather than 404) or
/// one not in review (rather than 409); only a valid reason reaches those answers (ADR 0013).
/// </summary>
public sealed class WorkflowEndpoints : IEndpoint
{
    /// <summary>
    /// The most bytes the return route reads of a body: a reason of 1000 characters, each escaped as <c>\uXXXX</c>
    /// (a character outside the BMP as two), is at most 12 000 bytes and its envelope a few more, so no valid reason is refused.
    /// </summary>
    private const int ReturnRequestLimit = 16_384;

    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapPost("/course-versions/{id:guid}/submit", (Guid id, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            ByEditorAsync(id, user, editor => dispatcher.SendAsync(new SubmitForReview(CourseVersionId.From(id), editor), cancellationToken)))
            .RequireAuthorization(EditorialPolicyNames.Edit);

        // The body is read here rather than bound, so that a missing or unreadable reason is refused with the module's
        // code like a blank one, and only after authorization, the caller's person and the caller's name. Accepts metadata would make
        // routing answer 415 to another content type before either, so the document's required body comes from an
        // operation transformer instead.
        endpoints.MapPost("/course-versions/{id:guid}/return", (Guid id, HttpRequest http, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            ByEditorAsync(id, user, editor => ReturnAsync(id, http, user, editor, dispatcher, cancellationToken)))
            .AddOpenApiOperationTransformer(DescribeReturnBodyAsync)
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(ReturnRequestLimit))
            .RequireAuthorization(EditorialPolicyNames.Edit);

        endpoints.MapPost("/course-versions/{id:guid}/publish", (Guid id, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            ByEditorAsync(id, user, editor => dispatcher.SendAsync(new PublishCourseVersion(CourseVersionId.From(id), editor), cancellationToken)))
            .RequireAuthorization(EditorialPolicyNames.Edit);

        endpoints.MapPost("/course-versions/{id:guid}/retire", (Guid id, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            ByEditorAsync(id, user, editor => dispatcher.SendAsync(new RetireCourseVersion(CourseVersionId.From(id), editor), cancellationToken)))
            .RequireAuthorization(EditorialPolicyNames.Edit);
    }

    /// <summary>
    /// A return by a caller without a usable name, or with a reason the caller got wrong, is refused before it is
    /// dispatched, the name first; any other is dispatched with both trimmed.
    /// </summary>
    private static async Task<Result<Unit, ApiError>> ReturnAsync(Guid id, HttpRequest http, ClaimsPrincipal user, PersonId editor, IDispatcher dispatcher, CancellationToken cancellationToken)
    {
        var name = EditorialCaller.NameOf(user);
        if (!name.HasValue)
        {
            return Failure(CourseVersionFailures.ReviewerNameMissing());
        }

        var request = await ReadAsync(http, cancellationToken);
        if (!ReturnReason.TryFrom(request?.Reason, out var reason))
        {
            return Failure(CourseVersionFailures.InvalidReturnReason());
        }

        return await dispatcher.SendAsync(new ReturnToDraft(CourseVersionId.From(id), editor, name.Value, reason), cancellationToken);
    }

    /// <summary>The return's required JSON body, <see cref="ReturnEditorialCourseVersionRequest"/>, as a component of the document.</summary>
    private static async Task DescribeReturnBodyAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken)
    {
        var name = nameof(ReturnEditorialCourseVersionRequest);
        var schema = await context.GetOrCreateSchemaAsync(typeof(ReturnEditorialCourseVersionRequest), null, cancellationToken);
        context.Document?.AddComponent(name, schema);
        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Content = new Dictionary<string, IOpenApiMediaType>
            {
                ["application/json"] = new OpenApiMediaType { Schema = new OpenApiSchemaReference(name, context.Document) },
            },
        };
    }

    /// <summary>
    /// The body, or null when it is no JSON in UTF-8 (JSON's own charset, the only one accepted here) or cannot be read
    /// as the request. A body larger than <see cref="ReturnRequestLimit"/> fails while it is read, and the host answers 413.
    /// </summary>
    private static async Task<ReturnEditorialCourseVersionRequest?> ReadAsync(HttpRequest http, CancellationToken cancellationToken)
    {
        if (!http.HasJsonContentType() || !IsUtf8(http))
        {
            return null;
        }

        var options = http.HttpContext.RequestServices.GetRequiredService<IOptions<JsonOptions>>().Value.SerializerOptions;
        try
        {
            return await JsonSerializer.DeserializeAsync<ReturnEditorialCourseVersionRequest>(http.Body, options, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>True for a content type that names no charset or names UTF-8, quoted or not.</summary>
    private static bool IsUtf8(HttpRequest http)
    {
        var charset = MediaTypeHeaderValue.Parse(http.ContentType).Charset;
        return !charset.HasValue
            || HeaderUtilities.RemoveQuotes(charset).Trim().Equals("utf-8", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A step the calling editor takes: the empty Guid is not found, a caller without a person is refused.</summary>
    private static async Task<Results<Ok<Unit>, ProblemHttpResult>> ByEditorAsync(Guid id, ClaimsPrincipal user, Func<PersonId, Task<Result<Unit, ApiError>>> step)
    {
        if (id == Guid.Empty)
        {
            return Result<Unit, ApiError>.Failure(CourseVersionFailures.NotFound(id)).ToHttpResult();
        }

        var editor = EditorialCaller.PersonOf(user);
        if (!editor.HasValue)
        {
            return Result<Unit, ApiError>.Failure(CourseVersionFailures.IdentityIncomplete()).ToHttpResult();
        }

        return (await step(editor.Value)).ToHttpResult();
    }
}
