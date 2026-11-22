using SmartSolutionsLab.PharmaTraining.BuildingBlocks.Messaging;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.Commands.ApplyAiTestDraft;

/// <summary>
/// An editor has the model draft a version's test and applies it (story E9): the unreviewed questions are replaced by
/// the drafted ones, the approved ones stay (ADR 0008 D10).
/// </summary>
public sealed record ApplyAiTestDraft(CourseVersionId Version) : ICommand<Result<EditorialTestDraftDto, ApiError>>;
