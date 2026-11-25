using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Certificates;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Memberships;
using SmartSolutionsLab.PharmaTraining.Modules.Certification.Domain.Retention;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Commands.SetRetentionPeriod;

/// <summary>The pharmacy's manager sets how many years the pharmacy keeps a certificate in its view (#391).</summary>
public sealed record SetRetentionPeriod(PharmacyId Pharmacy, RetentionPeriod Period, PersonId Caller) : ICommand<Result<Unit, ApiError>>;
