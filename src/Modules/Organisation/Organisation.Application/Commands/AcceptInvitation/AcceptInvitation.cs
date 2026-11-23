using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Organisation.Domain.Pharmacies;

namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Application.Commands.AcceptInvitation;

/// <summary>
/// The invited person accepts with the token from the link, as Keycloak knows them: <paramref name="Email"/> is the
/// account's address and whether Keycloak verified it; only a verified one may accept. Answers the pharmacy's id.
/// </summary>
public sealed record AcceptInvitation(InvitationToken Token, PersonId Person, DisplayName Name, AccountEmail Email) : ICommand<Result<Guid, ApiError>>;
