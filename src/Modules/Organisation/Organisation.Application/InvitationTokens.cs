using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application;

/// <summary>Creates the secret of an invitation link. Stub for #9's red tests (T3): always the same token.</summary>
public static class InvitationTokens
{
    public static InvitationToken Next() => InvitationToken.From(new string('a', InvitationToken.MinLength));
}
