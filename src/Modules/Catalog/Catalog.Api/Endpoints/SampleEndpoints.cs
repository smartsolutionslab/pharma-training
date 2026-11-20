using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.Commands.CreateSample;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.Queries.GetSample;
using SmartSolutionsLab.PharmaTraining.Modules.Catalog.Application.Queries.ListSamples;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Catalog.Api.Endpoints;

/// <summary>
/// The module's sample endpoints: create a sample, list them a page at a time, read one back. Rename them with the
/// sample or delete them (AGENTS.md).
/// </summary>
public sealed class SampleEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapPost("/samples", async (CreateCatalogSampleRequest request, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            (await dispatcher.SendAsync(new CreateSample(request.Name ?? string.Empty), cancellationToken)).ToCreatedHttpResult(Location)).RequireAuthorization(CatalogPolicyNames.Manage);
        endpoints.MapGet("/samples", async (Page? page, PageSize? pageSize, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            (await dispatcher.QueryAsync(new ListSamples(page ?? Page.First, pageSize ?? PageSize.Default), cancellationToken)).ToHttpResult());
        endpoints.MapGet("/samples/{id:guid}", async (Guid id, IDispatcher dispatcher, CancellationToken cancellationToken) =>
            (await dispatcher.QueryAsync(new GetSample(id), cancellationToken)).ToHttpResult());
    }

    private static string Location(Guid id) => $"/catalog/samples/{id}";
}
