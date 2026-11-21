using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Queries.ListCourseVersions;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using static SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions.CourseVersionBuilder;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.CourseVersions;

public class ListCourseVersionsTests
{
    [Fact]
    public async Task Versions_are_listed_by_status_with_how_many_there_are()
    {
        var store = new InMemoryCourseVersionStore();
        var draft = Draft("Arbeitsschutz");
        var published = Published("Betäubungsmittel");
        store.Stored.AddRange([draft, published, InReview("Lagerung")]);
        var handler = new ListCourseVersionsHandler(store);

        var drafts = await handler.HandleAsync(new ListCourseVersions(CourseVersionStatus.Draft, Page.First, PageSize.Default), TestContext.Current.CancellationToken);
        var publishedOnes = await handler.HandleAsync(new ListCourseVersions(CourseVersionStatus.Published, Page.First, PageSize.Default), TestContext.Current.CancellationToken);

        drafts.Value.Items.ShouldHaveSingleItem().ShouldBe(
            new EditorialCourseVersionSummaryDto(draft.Id.Value, draft.Course.Value, 1, "Arbeitsschutz", EditorialCourseVersionStatus.Draft));
        drafts.Value.TotalCount.ShouldBe(1);
        publishedOnes.Value.Items.ShouldHaveSingleItem().ShouldBe(
            new EditorialCourseVersionSummaryDto(published.Id.Value, published.Course.Value, 1, "Betäubungsmittel", EditorialCourseVersionStatus.Published));
    }

    [Fact]
    public async Task Without_a_status_every_version_is_listed_by_title_then_newest_number_first()
    {
        var store = new InMemoryCourseVersionStore();
        var first = Published("Hygiene 2027");
        var next = first.DraftNext(CourseVersionId.New(), Author, ChapterId.New, QuestionId.New, AnswerOptionId.New).Value;
        store.Stored.AddRange([first, Draft("Zytostatika"), next, Draft("Arbeitsschutz")]);

        var listed = await new ListCourseVersionsHandler(store).HandleAsync(new ListCourseVersions(null, Page.First, PageSize.Default), TestContext.Current.CancellationToken);

        listed.Value.Items.Select(version => (version.Title, version.Number)).ShouldBe(
            [("Arbeitsschutz", 1), ("Hygiene 2027", 2), ("Hygiene 2027", 1), ("Zytostatika", 1)]);
        listed.Value.TotalCount.ShouldBe(4);
    }

    [Fact]
    public async Task Versions_are_listed_a_page_at_a_time()
    {
        var store = new InMemoryCourseVersionStore();
        store.Stored.AddRange([Draft("Cleo"), Draft("Ada"), Draft("Bea")]);

        var page = await new ListCourseVersionsHandler(store).HandleAsync(new ListCourseVersions(CourseVersionStatus.Draft, PageOf(2), PageSizeOf(2)), TestContext.Current.CancellationToken);

        page.Value.Items.ShouldHaveSingleItem().Title.ShouldBe("Cleo");
        page.Value.TotalCount.ShouldBe(3);
        page.Value.Page.ShouldBe(2);
        page.Value.PageSize.ShouldBe(2);
    }

    [Fact]
    public async Task Without_versions_the_first_page_is_empty()
    {
        var page = await new ListCourseVersionsHandler(new InMemoryCourseVersionStore())
            .HandleAsync(new ListCourseVersions(null, Page.First, PageSize.Default), TestContext.Current.CancellationToken);

        page.Value.Items.ShouldBeEmpty();
        page.Value.TotalCount.ShouldBe(0);
    }

    private static Page PageOf(int value) =>
        Page.TryFrom(value, out var page) ? page : throw new InvalidOperationException($"{value} is not a page.");

    private static PageSize PageSizeOf(int value) =>
        PageSize.TryFrom(value, out var pageSize) ? pageSize : throw new InvalidOperationException($"{value} is not a page size.");
}
