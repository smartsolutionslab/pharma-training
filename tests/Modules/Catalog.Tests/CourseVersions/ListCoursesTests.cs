using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.Consumers;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.Queries.ListCourses;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Tests.CourseVersions;

public class ListCoursesTests
{
    [Fact]
    public async Task Courses_are_listed_by_title_a_page_at_a_time_as_their_current_version_with_how_many_there_are()
    {
        var store = new InMemoryCourseVersionStore();
        var consumer = new CourseVersionPublishedV1Consumer(store, store);
        var cleo = Guid.CreateVersion7();
        var cleoVersion = Guid.CreateVersion7();
        await consumer.HandleAsync(CourseVersionEvents.Published(cleo, cleoVersion, 4, "Cleo"), TestContext.Current.CancellationToken);
        await consumer.HandleAsync(CourseVersionEvents.Published(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, "Ada"), TestContext.Current.CancellationToken);
        await consumer.HandleAsync(CourseVersionEvents.Published(Guid.CreateVersion7(), Guid.CreateVersion7(), 1, "Bea"), TestContext.Current.CancellationToken);

        var page = await new ListCoursesHandler(store).HandleAsync(new ListCourses(PageOf(2), PageSizeOf(2)), TestContext.Current.CancellationToken);

        page.Value.Items.ShouldHaveSingleItem().ShouldBe(new CatalogCourseDto(cleo, cleoVersion, 4, "Cleo", 24));
        page.Value.TotalCount.ShouldBe(3);
        page.Value.Page.ShouldBe(2);
        page.Value.PageSize.ShouldBe(2);
    }

    [Fact]
    public async Task Without_courses_the_first_page_is_empty()
    {
        var page = await new ListCoursesHandler(new InMemoryCourseVersionStore()).HandleAsync(new ListCourses(Page.First, PageSize.Default), TestContext.Current.CancellationToken);

        page.Value.Items.ShouldBeEmpty();
        page.Value.TotalCount.ShouldBe(0);
        page.Value.Page.ShouldBe(Page.First.Value);
        page.Value.PageSize.ShouldBe(PageSize.Default.Value);
    }

    private static Page PageOf(int value) =>
        Page.TryFrom(value, out var page) ? page : throw new InvalidOperationException($"{value} is not a page.");

    private static PageSize PageSizeOf(int value) =>
        PageSize.TryFrom(value, out var pageSize) ? pageSize : throw new InvalidOperationException($"{value} is not a page size.");
}
