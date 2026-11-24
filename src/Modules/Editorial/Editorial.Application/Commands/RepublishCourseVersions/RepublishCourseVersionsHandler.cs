using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Storage;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.IntegrationEvents;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;
using Microsoft.Extensions.Logging;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.RepublishCourseVersions;

/// <summary>
/// Announces again every course version Editorial announced (ADR 0019): a version with <c>PublishedAt</c> set gets its
/// <c>CourseVersionPublishedV1</c> and then its <c>TestDefinitionPublishedV1</c>, then, when it has PDF or video chapters,
/// its <c>ChapterFilesReleasedV1</c> with read URLs signed now, the way the publish signs them (#264), since the URLs
/// first released lived fifteen minutes; and one with <c>RetiredAt</c> set its <c>CourseVersionRetiredV1</c> after them. The selection is by those fields, never by status, so a version marked
/// published by hand and never announced is not sent. The versions go by course, then by version number, and each is
/// committed on its own: it is loaded and its files signed first, since the outbox opens a transaction on the first publish and the load
/// must not run inside one, then its events are published and committed. A republished payload is the one the current
/// mappings make of the stored version, equal to the original while the mappings and
/// <see cref="EditorialIntegrationEventMappings.MaxAttempts"/> are unchanged; only the metadata is new, and the file
/// release's URLs and expiries, so consumers deduplicate on the version id (Learning's files on the name it derives
/// from the version and the chapter). Nothing tracked changes, so the commit cannot lose a race; a commit that fails throws,
/// what was committed before it stays sent, and a rerun is safe.
/// <para>
/// One run at a time: the run holds <see cref="IRepublishLock"/> from before its first load to its end, and a run asked
/// for meanwhile answers <c>editorial.republish-running</c> having sent nothing. Each version is committed with
/// <see cref="IEditorialUnitOfWork.CommitOneOfSeveralAsync"/>, so its events go out with its own commit. Every run logs
/// one line naming the editor: what it sent, or that it was refused; a run that fails or is cancelled logs a warning
/// with what it committed before it ended.
/// </para>
/// <para>
/// The answer counts what was sent. That is a deliberate exception to CQRS-logical's "a command returns at most an
/// identifier": only the command knows what it sent.
/// </para>
/// </summary>
public sealed partial class RepublishCourseVersionsHandler(
    ICourseVersionStore versions,
    IEditorialUnitOfWork unitOfWork,
    IBlobUrls urls,
    IRepublishLock runLock,
    ILogger<RepublishCourseVersionsHandler> logger)
    : ICommandHandler<RepublishCourseVersions, Result<EditorialRepublishedEventsDto, ApiError>>
{
    public async Task<Result<EditorialRepublishedEventsDto, ApiError>> HandleAsync(RepublishCourseVersions command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var taken = await runLock.TryTakeAsync(cancellationToken);
        if (!taken.HasValue)
        {
            Refused(logger, command.Editor.Value);
            return Failure(CourseVersionFailures.RepublishRunning());
        }

        await using var held = taken.Value;
        var courseVersions = 0;
        var testDefinitions = 0;
        var fileReleases = 0;
        var retirements = 0;
        var completed = false;
        try
        {
            var announced = await versions.ListAnnouncedAsync(cancellationToken);
            foreach (var id in announced)
            {
                var found = await versions.FindAsync(id, cancellationToken);
                if (!found.HasValue)
                {
                    continue;
                }

                var version = found.Value;
                var fileRelease = false;
                if (version.PublishedAt is not null)
                {
                    var readUrls = await urls.SignReadUrlsAsync(version, cancellationToken);
                    await unitOfWork.PublishAsync(version.ToCourseVersionPublishedV1());
                    await unitOfWork.PublishAsync(version.ToTestDefinitionPublishedV1());
                    if (readUrls.Count > 0)
                    {
                        await unitOfWork.PublishAsync(version.ToChapterFilesReleasedV1(readUrls));
                        fileRelease = true;
                    }
                }

                if (version.RetiredAt is not null)
                {
                    await unitOfWork.PublishAsync(version.ToCourseVersionRetiredV1());
                }

                await unitOfWork.CommitOneOfSeveralAsync(cancellationToken);

                // Counted once committed, so a run that ends early logs only what went out.
                if (version.PublishedAt is not null)
                {
                    courseVersions++;
                    testDefinitions++;
                }

                if (fileRelease)
                {
                    fileReleases++;
                }

                if (version.RetiredAt is not null)
                {
                    retirements++;
                }
            }

            completed = true;
        }
        finally
        {
            Log(command.Editor, new RepublishedEvents(courseVersions, testDefinitions, fileReleases, retirements), completed);
        }

        return Success(new RepublishedEvents(courseVersions, testDefinitions, fileReleases, retirements).ToDto());
    }

    /// <summary>
    /// The run's log line, written however the run ends: the editor and every count of what was committed, as the record
    /// prints them; a warning when the run failed or was cancelled before its end.
    /// </summary>
    private void Log(PersonId editor, RepublishedEvents sent, bool completed)
    {
        if (completed)
        {
            Republished(logger, editor.Value, sent);
            return;
        }

        EndedEarly(logger, editor.Value, sent);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Editor {Editor} republished Editorial's course-version events: {Sent}.")]
    private static partial void Republished(ILogger logger, Guid editor, RepublishedEvents sent);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Editor {Editor}'s republish of Editorial's course-version events ended before its end; committed and sent before: {Sent}.")]
    private static partial void EndedEarly(ILogger logger, Guid editor, RepublishedEvents sent);

    [LoggerMessage(Level = LogLevel.Information, Message = "Editor {Editor} asked for a republish while another run was running; refused, nothing sent.")]
    private static partial void Refused(ILogger logger, Guid editor);
}
