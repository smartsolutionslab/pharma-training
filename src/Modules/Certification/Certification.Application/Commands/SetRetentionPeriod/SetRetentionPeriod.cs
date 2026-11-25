using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Commands.SetRetentionPeriod;

/// <summary>
/// The pharmacy's manager sets how many years the pharmacy keeps a certificate in its view (#391). It carries the
/// request as received (#422): who called, which pharmacy and the years, each of which the handler judges in turn.
/// </summary>
public sealed record SetRetentionPeriod(Option<PharmacyId> Pharmacy, RequestedRetentionYears Years, Option<PersonId> Caller)
    : ICommand<Result<Unit, ApiError>>;
