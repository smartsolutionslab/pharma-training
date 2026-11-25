using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>
/// The people's accounts in Keycloak, by their subject, which is their <see cref="PersonId"/>; implemented in the
/// Infrastructure project over Keycloak's admin API. A failure of Keycloak throws <see cref="AccountsUnavailableException"/>
/// or <see cref="HttpRequestException"/>.
/// </summary>
public interface IAccounts
{
    /// <summary>The person's account, or none when Keycloak has no account for them.</summary>
    Task<Option<Account>> FindAsync(PersonId person, CancellationToken cancellationToken);

    /// <summary>Deletes the person's account; an account that is gone already counts as deleted.</summary>
    Task DeleteAsync(PersonId person, CancellationToken cancellationToken);
}
