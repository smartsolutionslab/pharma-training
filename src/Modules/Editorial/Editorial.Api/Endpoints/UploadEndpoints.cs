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

        endpoints.MapPost("/course-versions/{id:guid}/uploads", async (Guid id, EditorialUploadRequest request, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            // A body without a kind binds to Text, which takes no file; the converter also reads numbers, so an undefined
            // one is refused here too.
            var kind = request.Kind;
            if (!Enum.IsDefined(kind) || kind == EditorialChapterRequestKind.Text)
            {
                return Result<EditorialChapterUploadDto, ApiError>.Failure(CourseVersionFailures.InvalidUploadKind()).ToHttpResult();
            }

            return (await dispatcher.QueryAsync(new RequestChapterUpload(id, kind.ToEditorialChapterKind()), cancellationToken)).ToHttpResult();
        }).RequireAuthorization(EditorialPolicyNames.Edit);
    }
}
