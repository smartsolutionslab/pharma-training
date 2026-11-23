using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.IntegrationEvents;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RepublishCourseVersions;

/// <summary>
/// Announces again every course version Editorial announced (ADR 0019): a version with <c>PublishedAt</c> set gets its
/// <c>CourseVersionPublishedV1</c> and then its <c>TestDefinitionPublishedV1</c>, and one with <c>RetiredAt</c> set its
/// <c>CourseVersionRetiredV1</c> after them. The selection is by those fields, never by status, so a version marked
/// published by hand and never announced is not sent. The versions go by course, then by version number, and each is
/// committed on its own: it is loaded first, since the outbox opens a transaction on the first publish and the load
/// must not run inside one, then its events are published and committed. A republished payload is the one the current
/// mappings make of the stored version, equal to the original while the mappings and
/// <see cref="EditorialIntegrationEventMappings.MaxAttempts"/> are unchanged; only the metadata is new, so consumers
/// deduplicate on the version id. Nothing tracked changes, so the commit cannot lose a race; a commit that fails throws,
/// what was committed before it stays sent, and a rerun is safe.
/// <para>
/// The answer counts what was sent. That is a deliberate exception to CQRS-logical's "a command returns at most an
/// identifier": only the command knows what it sent.
/// </para>
/// </summary>
public sealed class RepublishCourseVersionsHandler(ICourseVersionStore versions, IEditorialUnitOfWork unitOfWork)
    : ICommandHandler<RepublishCourseVersions, Result<EditorialRepublishedEventsDto, ApiError>>
{
    public async Task<Result<EditorialRepublishedEventsDto, ApiError>> HandleAsync(RepublishCourseVersions command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var announced = await versions.ListAnnouncedAsync(cancellationToken);
        var courseVersions = 0;
        var testDefinitions = 0;
        var retirements = 0;
        foreach (var id in announced)
        {
            var found = await versions.FindAsync(id, cancellationToken);
            if (!found.HasValue)
            {
                continue;
            }

            var version = found.Value;
            if (version.PublishedAt is not null)
            {
                await unitOfWork.PublishAsync(version.ToCourseVersionPublishedV1());
                await unitOfWork.PublishAsync(version.ToTestDefinitionPublishedV1());
                courseVersions++;
                testDefinitions++;
            }

            if (version.RetiredAt is not null)
            {
                await unitOfWork.PublishAsync(version.ToCourseVersionRetiredV1());
                retirements++;
            }

            await unitOfWork.CommitAsync(cancellationToken);
        }

        return Success(new RepublishedEvents(courseVersions, testDefinitions, retirements).ToDto());
    }
}
