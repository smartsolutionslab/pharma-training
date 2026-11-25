using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Certification.Application.Commands.PurgeErasedCertificates;

/// <summary>
/// Deletes the retained certificates of erased persons that no covering pharmacy keeps any longer (#115, slice 5), sent
/// every interval by a <see cref="PeriodicCommandService{TCommand}"/>.
/// </summary>
public sealed record PurgeErasedCertificates : ICommand<Result<Unit, ApiError>>;
