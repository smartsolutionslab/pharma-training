using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.ReannounceExhaustions;

/// <summary>
/// One pass of Assessment's sweep of locks (#307, docs/adr/0016). A submit stores its ExaminationExhausted before it
/// commits the announcement; a crash or failure in between leaves the lock stored and never announced, and the learner
/// locked for good. Every stored ExaminationExhausted without a marker, stored between the lookback and the grace period
/// ago, is announced by <see cref="AnnouncementSweepPass"/> as the submit would have announced it (AttemptsExhaustedV1,
/// the same mapping, with the course of its examination), with its marker, in a commit of its own. The marker guarantees
/// that a lock whose announcement left is never announced again, so Learning, which notes each lock by its
/// EventIdentifier (docs/adr/0025), sees one; Compliance orders it by the sweep's OccurredAt (docs/adr/0023).
/// </summary>
public sealed class ReannounceExhaustionsHandler(
    IUnannouncedExaminationEvents<ExaminationExhausted> unannounced,
    IAnnouncedEvents announced,
    IAssessmentUnitOfWork unitOfWork,
    [FromKeyedServices(typeof(ReannounceExhaustions))] AnnouncementSweepSettings settings,
    TimeProvider time,
    ILogger<ReannounceExhaustionsHandler> logger)
    : ICommandHandler<ReannounceExhaustions, Result<Unit, ApiError>>
{
    private const string EventType = nameof(ExaminationExhausted);

    public async Task<Result<Unit, ApiError>> HandleAsync(ReannounceExhaustions command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var pass = new AnnouncementSweepPass(EventType, unitOfWork, announced.Mark, settings, time, logger);
        await pass.RunAsync(unannounced.FindAsync, AnnouncementOfAsync, cancellationToken);
        return Success(Unit.Value);
    }

    private async Task<Option<IntegrationEvent>> AnnouncementOfAsync(UnannouncedEvent found, CancellationToken cancellationToken)
    {
        var stored = await unannounced.LoadAsync(found, cancellationToken);
        return Option<IntegrationEvent>.Some(stored.Event.ToIntegrationEvent(stored.Examination));
    }
}
