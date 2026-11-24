using Microsoft.Extensions.AI;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ApplyAiTestDraft;

/// <summary>
/// One tracked load; the model is asked about that instance and its draft applied to it. Nothing is held while the
/// model answers: the version's row version refuses the save if the version changed meanwhile (ADR 0002), which costs
/// the model call. Exceptions from the model's client are not caught (ADR 0008 D8), and nothing is saved then.
/// </summary>
public sealed class ApplyAiTestDraftHandler(ICourseVersionStore versions, IEditorialUnitOfWork unitOfWork, IChatClient model)
    : ICommandHandler<ApplyAiTestDraft, Result<EditorialTestDraftDto, ApiError>>
{
    public async Task<Result<EditorialTestDraftDto, ApiError>> HandleAsync(ApplyAiTestDraft command, CancellationToken cancellationToken)
    {
        Ensure.That(command).IsNotNull();

        var found = await versions.FindForUpdateAsync(command.Version, cancellationToken);
        if (!found.HasValue)
        {
            return Failure(CourseVersionFailures.NotFound(command.Version.Value));
        }

        var version = found.Value;
        var draft = await TestDrafter.DraftAsync(model, version, cancellationToken);
        if (draft.IsFailure)
        {
            return Failure(draft.Error);
        }

        var applied = version.ApplyAiTestDraft(draft.Value.Questions, QuestionId.New, AnswerOptionId.New);
        if (applied.IsFailure)
        {
            return Failure(CourseVersionFailures.From(applied.Error));
        }

        var saved = await unitOfWork.TryCommitAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Failure(CourseVersionFailures.LostRace(saved.Error, command.Version.Value));
        }

        return Success(new AppliedTestDraft(applied.Value, draft.Value.LeftOutChapters).ToDto());
    }
}
