using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>An applied AI test draft as the module's endpoint answers it.</summary>
public static class AppliedTestDraftMappings
{
    public static EditorialTestDraftDto ToDto(this AppliedTestDraft draft)
    {
        Ensure.That(draft).IsNotNull();
        return new EditorialTestDraftDto([.. draft.Questions.Select(id => id.Value)], [.. draft.LeftOutChapters.Select(id => id.Value)]);
    }
}
