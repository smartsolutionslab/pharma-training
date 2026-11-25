using Microsoft.Extensions.Options;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Infrastructure;

/// <summary>
/// The people's accounts over Keycloak's admin API, as the client <c>pharma-training-erasure</c>: it mints a token with
/// the client credentials grant for each call, then reads or deletes <c>/admin/realms/{realm}/users/{id}</c>.
/// </summary>
public sealed class KeycloakAccounts(HttpClient http, IOptions<KeycloakAccountsSettings> settings) : IAccounts
{
    private readonly HttpClient http = http;
    private readonly IOptions<KeycloakAccountsSettings> settings = settings;

    public Task<Option<Account>> FindAsync(PersonId person, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task DeleteAsync(PersonId person, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
