using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.Queries.GetCourseVersion;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.Queries.ListCourses;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Api.Endpoints;

/// <summary>
/// The catalog: the courses as their current version, a page at a time, and any course version by its id. Open to
/// every signed-in user through the module group's authorization: published courses are shared by all pharmacies and
/// carry no questions, and Catalog cannot know a pharmacy's roles.
/// </summary>
public sealed class CourseEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapGet("/courses", async (Page? page, PageSize? pageSize, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            (await dispatcher.QueryAsync(new ListCourses(page ?? Page.First, pageSize ?? PageSize.Default), cancellationToken)).ToHttpResult());
        endpoints.MapGet("/course-versions/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            (await dispatcher.QueryAsync(new GetCourseVersion(id), cancellationToken)).ToHttpResult());
    }
}
