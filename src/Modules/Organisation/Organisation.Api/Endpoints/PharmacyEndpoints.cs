using System.Security.Claims;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Api;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.RegisterPharmacy;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Queries.GetMyPharmacy;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Queries.ListMyPharmacies;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Api.Endpoints;

/// <summary>
/// Register a pharmacy, list the pharmacies I am an active member of, and read one of them. Any signed-in user may
/// register one; who sees which pharmacy is decided by their membership, not by a role.
/// </summary>
public sealed class PharmacyEndpoints : IEndpoint
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        Ensure.That(endpoints).IsNotNull();

        endpoints.MapPost("/pharmacies", async (RegisterPharmacyRequest request, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            // What the caller got wrong is refused before it becomes a value object, whose factory would throw on it.
            var caller = CallerClaims.Of(user);
            if (!caller.HasValue)
            {
                return Result<Guid, ApiError>.Failure(PharmacyFailures.IdentityIncomplete()).ToCreatedHttpResult(Location);
            }

            if (!PharmacyName.TryFrom(request.Name, out var name))
            {
                return Result<Guid, ApiError>.Failure(PharmacyFailures.InvalidName()).ToCreatedHttpResult(Location);
            }

            // The enum's converter also reads numbers, so an undefined one is the caller's mistake too, not a bug.
            if (request.JobFunction is not { } jobFunction || !Enum.IsDefined(jobFunction))
            {
                return Result<Guid, ApiError>.Failure(PharmacyFailures.InvalidJobFunction()).ToCreatedHttpResult(Location);
            }

            var founder = caller.Value;
            var command = new RegisterPharmacy(name, jobFunction.ToDomain(), founder.Person, founder.Name, founder.Email);
            return (await dispatcher.SendAsync(command, cancellationToken)).ToCreatedHttpResult(Location);
        }).RequireAuthorization();

        endpoints.MapGet("/pharmacies/mine", async (Page? page, PageSize? pageSize, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            var person = CallerClaims.PersonOf(user);
            if (!person.HasValue)
            {
                return Result<PagedResult<MyPharmacyDto>, ApiError>.Failure(PharmacyFailures.IdentityIncomplete()).ToHttpResult();
            }

            return (await dispatcher.QueryAsync(new ListMyPharmacies(person.Value, page ?? Page.First, pageSize ?? PageSize.Default), cancellationToken)).ToHttpResult();
        }).RequireAuthorization();

        endpoints.MapGet("/pharmacies/{id:guid}", async (Guid id, ClaimsPrincipal user, IDispatcher dispatcher, CancellationToken cancellationToken) =>
        {
            var person = CallerClaims.PersonOf(user);
            if (!person.HasValue)
            {
                return Result<MyPharmacyDto, ApiError>.Failure(PharmacyFailures.IdentityIncomplete()).ToHttpResult();
            }

            return (await dispatcher.QueryAsync(new GetMyPharmacy(id, person.Value), cancellationToken)).ToHttpResult();
        }).RequireAuthorization();
    }

    private static string Location(Guid id) => $"/organisation/pharmacies/{id}";
}
