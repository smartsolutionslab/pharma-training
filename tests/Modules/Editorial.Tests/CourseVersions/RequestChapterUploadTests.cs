using System.Net;
using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.RequestChapterUpload;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// An editor asks where to upload a chapter's PDF or video: a draft answers an upload URL for a name the module chose,
/// with the content type and size its kind takes; nothing is stored.
/// </summary>
public class RequestChapterUploadTests
{
    private static readonly DateTimeOffset Noon = new(2027, 3, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider time = new(Noon);
    private readonly InMemoryCourseVersionStore store = new();
    private readonly FakeBlobUrls urls;

    public RequestChapterUploadTests() => urls = new FakeBlobUrls(time);

    [Theory]
    [InlineData(EditorialChapterKind.Pdf, ".pdf", "application/pdf", 52_428_800L)]
    [InlineData(EditorialChapterKind.Video, ".mp4", "video/mp4", 2_147_483_648L)]
    public async Task A_draft_answers_an_upload_url_for_a_name_of_its_own_with_the_content_type_and_size_of_the_kind(
        EditorialChapterKind kind, string extension, string contentType, long maxBytes)
    {
        var version = Draft();
        store.Stored.Add(version);

        var answered = await Handler().HandleAsync(new RequestChapterUpload(version.Id.Value, kind), TestContext.Current.CancellationToken);

        answered.IsSuccess.ShouldBeTrue(answered.IsFailure ? answered.Error.Code : string.Empty);
        var upload = answered.Value;
        upload.BlobName.ShouldStartWith($"course-versions/{version.Id.Value:D}/");
        upload.BlobName.ShouldEndWith(extension);
        var signed = urls.Uploads.ShouldHaveSingleItem();
        signed.Name.Value.ShouldBe(upload.BlobName);
        upload.ShouldBe(new EditorialChapterUploadDto(
            upload.BlobName,
            urls.UrlFor(signed.Name, signed.Lifetime, "c").Url,
            Noon.AddMinutes(15),
            contentType,
            maxBytes));
        urls.Reads.ShouldBeEmpty();
        store.Commits.ShouldBe(0);
    }

    [Fact]
    public async Task The_upload_url_lives_fifteen_minutes()
    {
        var version = Draft();
        store.Stored.Add(version);

        var answered = await Handler().HandleAsync(new RequestChapterUpload(version.Id.Value, EditorialChapterKind.Pdf), TestContext.Current.CancellationToken);

        answered.IsSuccess.ShouldBeTrue(answered.IsFailure ? answered.Error.Code : string.Empty);
        urls.Uploads.ShouldHaveSingleItem().Lifetime.ShouldBe(BlobUrlLifetime.From(BlobUrlLifetime.Maximum));
        answered.Value.ExpiresAt.ShouldBe(Noon.AddMinutes(15));
    }

    [Fact]
    public async Task Two_requests_get_two_names()
    {
        var version = Draft();
        store.Stored.Add(version);

        var first = await Handler().HandleAsync(new RequestChapterUpload(version.Id.Value, EditorialChapterKind.Pdf), TestContext.Current.CancellationToken);
        var second = await Handler().HandleAsync(new RequestChapterUpload(version.Id.Value, EditorialChapterKind.Pdf), TestContext.Current.CancellationToken);

        first.IsSuccess.ShouldBeTrue(first.IsFailure ? first.Error.Code : string.Empty);
        second.IsSuccess.ShouldBeTrue(second.IsFailure ? second.Error.Code : string.Empty);
        second.Value.BlobName.ShouldNotBe(first.Value.BlobName);
    }

    [Fact]
    public async Task A_version_that_does_not_exist_is_not_found_and_nothing_is_signed()
    {
        var answered = await Handler().HandleAsync(new RequestChapterUpload(Guid.CreateVersion7(), EditorialChapterKind.Pdf), TestContext.Current.CancellationToken);

        answered.IsFailure.ShouldBeTrue();
        answered.Error.Code.ShouldBe("editorial.course-version-not-found");
        answered.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        urls.Uploads.ShouldBeEmpty();
    }

    [Fact]
    public async Task The_empty_guid_is_no_version()
    {
        var answered = await Handler().HandleAsync(new RequestChapterUpload(Guid.Empty, EditorialChapterKind.Pdf), TestContext.Current.CancellationToken);

        answered.IsFailure.ShouldBeTrue();
        answered.Error.Code.ShouldBe("editorial.course-version-not-found");
        answered.Error.Status.ShouldBe(HttpStatusCode.NotFound);
        urls.Uploads.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_version_in_review_or_published_gets_no_upload_url()
    {
        var inReview = InReview();
        var published = Published();
        store.Stored.AddRange([inReview, published]);

        foreach (var version in new[] { inReview, published })
        {
            var answered = await Handler().HandleAsync(new RequestChapterUpload(version.Id.Value, EditorialChapterKind.Video), TestContext.Current.CancellationToken);

            answered.IsFailure.ShouldBeTrue($"a version {version.Status} gets no upload url");
            answered.Error.Code.ShouldBe("editorial.not-a-draft");
            answered.Error.Status.ShouldBe(HttpStatusCode.Conflict);
        }

        urls.Uploads.ShouldBeEmpty();
    }

    private RequestChapterUploadHandler Handler() => new(store, urls);
}
