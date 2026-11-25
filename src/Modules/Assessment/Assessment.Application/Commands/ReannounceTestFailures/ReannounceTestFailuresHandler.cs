using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.ReannounceTestFailures;

/// <summary>
/// One pass of Assessment's sweep of failures (#307, docs/adr/0016). A submit stores its AttemptSubmitted before it
/// commits the announcement; a crash or failure in between leaves a failed attempt stored and never announced. Every
/// stored AttemptSubmitted without a marker, stored between the lookback and the grace period ago, is settled by
/// <see cref="AnnouncementSweepPass"/>: a failed one is announced as the submit would have announced it (TestFailedV1,
/// the same mapping, with the attempt's number from its examination), with its marker, in a commit of its own. A passed
/// one is announced by its ExaminationPassed, which the sweep of passes looks after, so it gets its marker alone and is
/// never read again. One that cannot be read or mapped is skipped, as in every sweep.
/// </summary>
public sealed class ReannounceTestFailuresHandler(
    IUnannouncedExaminationEvents<AttemptSubmitted> unannounced,
    IAnnouncedEvents announced,
    IAssessmentUnitOfWork unitOfWork,
    [FromKeyedServices(typeof(ReannounceTestFailures))] AnnouncementSweepSettings settings,
    TimeProvider time,
    ILogger<ReannounceTestFailuresHandler> logger)
    : ICommandHandler<ReannounceTestFailures, Result<Unit, ApiError>>
{
    private const string EventType = nameof(AttemptSubmitted);

    public async Task<Result<Unit, ApiError>> HandleAsync(ReannounceTestFailures command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var pass = new AnnouncementSweepPass(EventType, unitOfWork, announced.Mark, settings, time, logger);
        await pass.RunAsync(unannounced.FindAsync, AnnouncementOfAsync, cancellationToken);
        return Success(Unit.Value);
    }

    /// <summary>TestFailedV1 for a failed attempt; the marker alone for a passed one, whose announcement is its ExaminationPassed's.</summary>
    private async Task<SweptEvent> AnnouncementOfAsync(UnannouncedEvent found, CancellationToken cancellationToken)
    {
        var stored = await unannounced.LoadAsync(found, cancellationToken);
        if (stored.Event.Outcome != AttemptOutcome.Failed)
        {
            return SweptEvents.MarkOnly();
        }

        return SweptEvents.Announce(stored.Event.ToIntegrationEvent(stored.Examination));
    }
}
