using System.Text.Json;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.RequestChapterUpload;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>
/// An editor asks where the browser uploads a draft's PDF or video: 200 with a short-lived URL, since nothing of this
/// API is created; the chapter then names the upload's blob as its file.
/// </summary>
public sealed class UploadEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        // The body is read here rather than bound, so that a kind the converter cannot read (Text, which takes no file)
        // is refused with the module's code like a missing or undefined one, instead of the framework's bare 400. The
        // document describes it as EditorialUploadRequest, whose kind names exactly what is accepted: Pdf or Video.
        endpoints.MapPost("/course-versions/{id:guid}/uploads", async (Guid id, HttpRequest http, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            var request = await ReadAsync(http, cancellationToken);
            if (request is not { Kind: var kind } || !Enum.IsDefined(kind))
            {
                return Result<EditorialChapterUploadDto, ApiError>.Failure(CourseVersionFailures.InvalidUploadKind()).ToHttpResult();
            }

            return (await dispatcher.QueryAsync(new RequestChapterUpload(id, kind.ToEditorialChapterKind()), cancellationToken)).ToHttpResult();
        })
        .Accepts<EditorialUploadRequest>("application/json")
        .RequireAuthorization(EditorialPolicyNames.Edit);
    }

    /// <summary>
    /// The body, or null when it is no JSON or names a kind that is no upload's. A body without a kind reads as 0, which
    /// is no kind either; the converter also reads numbers, so an undefined one is left to the caller's check.
    /// </summary>
    private static async Task<EditorialUploadRequest?> ReadAsync(HttpRequest http, CancellationToken cancellationToken)
    {
        if (!http.HasJsonContentType())
        {
            return null;
        }

        try
        {
            return await http.ReadFromJsonAsync<EditorialUploadRequest>(cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
