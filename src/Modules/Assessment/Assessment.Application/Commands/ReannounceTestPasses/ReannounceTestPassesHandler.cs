using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Contracts;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.IntegrationEvents;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.ReannounceTestPasses;

/// <summary>
/// One pass of Assessment's announcement sweep (#277, docs/adr/0016). A submit stores its ExaminationPassed before it
/// commits the announcement; a crash or failure in between leaves the pass stored and never announced. Every stored
/// ExaminationPassed without a marker, stored between the lookback and the grace period ago, is announced as the submit
/// would have announced it (TestPassedV1, the same mapping, so PassedAt is the stored one), oldest first and at most a
/// batch a pass, each with its marker, keyed on the stream the event is stored in, in a commit of its own, and logged
/// as a Warning naming the stream and version. One that cannot be read or mapped is skipped, and the pass reads on past
/// it: it reads a page of a batch at a time, each after the last event it read, until it has announced a batch, reached
/// the end of the window or read AnnouncementSweepSettings.MostPagesPerPass pages (#337). The pass then logs one
/// Warning for all it skipped, naming how many, the first and the last, and the first failure's type (#360). Of two
/// writers that announce the same event (a replica's sweep, the late submit), the marker's primary key lets one commit
/// through; the other throws, and the hosted service logs it. The pass has no failure of its own: anything else throws.
/// The pass itself is <see cref="AnnouncementSweepPass"/>, which the sweeps of failures and locks run too (#307).
/// </summary>
public sealed class ReannounceTestPassesHandler(
    IUnannouncedTestPasses unannounced,
    IAnnouncedEvents announced,
    IAssessmentUnitOfWork unitOfWork,
    [FromKeyedServices(typeof(ReannounceTestPasses))] AnnouncementSweepSettings settings,
    TimeProvider time,
    ILogger<ReannounceTestPassesHandler> logger)
    : ICommandHandler<ReannounceTestPasses, Result<Unit, ApiError>>
{
    private const string EventType = nameof(Domain.Examinations.Events.ExaminationPassed);

    public async Task<Result<Unit, ApiError>> HandleAsync(ReannounceTestPasses command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var pass = new AnnouncementSweepPass(EventType, unitOfWork, announced.Mark, settings, time, logger);
        await pass.RunAsync(unannounced.FindAsync, AnnouncementOfAsync, cancellationToken);
        return Success(Unit.Value);
    }

    /// <summary>The announcement of a stored test pass: TestPassedV1, the submit's mapping, so PassedAt is the stored one.</summary>
    private async Task<Option<IntegrationEvent>> AnnouncementOfAsync(UnannouncedEvent stored, CancellationToken cancellationToken)
    {
        var passed = await unannounced.LoadAsync(stored, cancellationToken);
        return Option<IntegrationEvent>.Some(passed.ToIntegrationEvent());
    }
}
