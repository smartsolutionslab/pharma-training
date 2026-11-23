using System.Net;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.ListCourseVersionReturns;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

/// <summary>
/// A version's returns are listed a page at a time, newest first, each with who returned it, when and why; a version
/// never returned lists an empty page, and an unknown one is not found (#181, D5). The records are made by the
/// version's own <c>ReturnToDraft</c> and committed through the store, as the return use case does.
/// </summary>
public class ListCourseVersionReturnsTests
{
    private static readonly ReturnReason First = ReturnReason.From("Erste Rückgabe: Quelle fehlt.");
    private static readonly ReturnReason Second = ReturnReason.From("Zweite Rückgabe: Frage 1 ist mehrdeutig.");
    private static readonly ReturnReason Third = ReturnReason.From("Dritte Rückgabe: Kapitel 2 zu lang.");

    private readonly InMemoryCourseVersionStore store = new();

    [Fact]
    public async Task The_returns_are_listed_newest_first()
    {
        var version = StoredInReview();
        var earlier = await ReturnAsync(version, Reviewer, First, Now);
        var later = await ReturnAsync(version, Approver, Second, Timestamp.From(Now.Value.AddHours(1)));

        var listed = await ListAsync(version.Id.Value, Page.First, PageSize.Default);

        listed.IsSuccess.ShouldBeTrue();
        listed.Value.Items.Select(item => item.Id).ShouldBe([later.Id.Value, earlier.Id.Value]);
        listed.Value.Items[0].Reason.ShouldBe(Second.Value);
        listed.Value.Items[0].ReturnedBy.ShouldBe(Approver.Value);
        listed.Value.Items[1].Reason.ShouldBe(First.Value);
        listed.Value.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task A_page_holds_at_most_its_size_and_counts_every_return()
    {
        var version = StoredInReview();
        var oldest = await ReturnAsync(version, Reviewer, First, Now);
        await ReturnAsync(version, Reviewer, Second, Timestamp.From(Now.Value.AddHours(1)));
        await ReturnAsync(version, Reviewer, Third, Timestamp.From(Now.Value.AddHours(2)));

        var page = await ListAsync(version.Id.Value, PageOf(2), PageSizeOf(2));

        page.Value.Items.ShouldHaveSingleItem().Id.ShouldBe(oldest.Id.Value);
        page.Value.TotalCount.ShouldBe(3);
        page.Value.Page.ShouldBe(2);
        page.Value.PageSize.ShouldBe(2);
    }

    [Fact]
    public async Task Only_the_versions_own_returns_are_listed()
    {
        var one = StoredInReview();
        var other = StoredInReview();
        var ofOne = await ReturnAsync(one, Reviewer, First, Now);
        var ofOther = await ReturnAsync(other, Reviewer, Second, Now);

        var listedOne = await ListAsync(one.Id.Value, Page.First, PageSize.Default);
        var listedOther = await ListAsync(other.Id.Value, Page.First, PageSize.Default);

        listedOne.Value.Items.ShouldHaveSingleItem().Id.ShouldBe(ofOne.Id.Value);
        listedOne.Value.TotalCount.ShouldBe(1);
        listedOther.Value.Items.ShouldHaveSingleItem().Id.ShouldBe(ofOther.Id.Value);
        listedOther.Value.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task A_version_never_returned_lists_an_empty_page()
    {
        var version = StoredInReview();

        var listed = await ListAsync(version.Id.Value, Page.First, PageSize.Default);

        listed.IsSuccess.ShouldBeTrue();
        listed.Value.Items.ShouldBeEmpty();
        listed.Value.TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task The_returns_of_an_unknown_version_are_not_found()
    {
        var listed = await ListAsync(Guid.CreateVersion7(), Page.First, PageSize.Default);

        listed.Error.Code.ShouldBe("editorial.course-version-not-found");
        listed.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_returns_of_the_empty_guid_are_not_found()
    {
        var listed = await ListAsync(Guid.Empty, Page.First, PageSize.Default);

        listed.Error.Code.ShouldBe("editorial.course-version-not-found");
        listed.Error.Status.ShouldBe(HttpStatusCode.NotFound);
    }

    private static Page PageOf(int value) =>
        Page.TryFrom(value, out var page) ? page : throw new InvalidOperationException($"{value} is not a page.");

    private static PageSize PageSizeOf(int value) =>
        PageSize.TryFrom(value, out var pageSize) ? pageSize : throw new InvalidOperationException($"{value} is not a page size.");

    private CourseVersion StoredInReview()
    {
        var version = InReview();
        store.Stored.Add(version);
        return version;
    }

    /// <summary>Returns the version as the use case does, record added and committed, then submits it again for the next return.</summary>
    private async Task<CourseVersionReturn> ReturnAsync(CourseVersion version, PersonId editor, ReturnReason reason, Timestamp at)
    {
        var record = version.ReturnToDraft(editor, reason, at).Value;
        store.AddReturn(record);
        await store.CommitAsync(TestContext.Current.CancellationToken);
        version.SubmitForReview(Author).IsSuccess.ShouldBeTrue();
        return record;
    }

    private Task<Result<PagedResult<EditorialCourseVersionReturnDto>, ApiError>> ListAsync(Guid version, Page page, PageSize pageSize) =>
        new ListCourseVersionReturnsHandler(store).HandleAsync(new ListCourseVersionReturns(version, page, pageSize), TestContext.Current.CancellationToken);
}
