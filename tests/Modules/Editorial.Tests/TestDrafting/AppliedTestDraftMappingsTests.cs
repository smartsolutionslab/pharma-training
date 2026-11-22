using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.TestDrafting;

/// <summary>An applied AI test draft travels as the ids of the questions it drafted and of the chapters it left out, in their order.</summary>
public class AppliedTestDraftMappingsTests
{
    [Fact]
    public void An_applied_draft_maps_to_its_question_and_left_out_chapter_ids_in_order()
    {
        var first = QuestionId.New();
        var second = QuestionId.New();
        var third = QuestionId.New();
        var video = ChapterId.New();
        var pdf = ChapterId.New();

        var dto = new AppliedTestDraft([third, first, second], [video, pdf]).ToDto();

        dto.ShouldBeOfType<EditorialTestDraftDto>();
        dto.DraftedQuestionIds.ShouldBe([third.Value, first.Value, second.Value]);
        dto.LeftOutChapterIds.ShouldBe([video.Value, pdf.Value]);
    }

    [Fact]
    public void An_applied_draft_that_left_no_chapter_out_maps_to_an_empty_list_not_null()
    {
        var question = QuestionId.New();

        var dto = new AppliedTestDraft([question], []).ToDto();

        dto.DraftedQuestionIds.ShouldBe([question.Value]);
        dto.LeftOutChapterIds.ShouldNotBeNull();
        dto.LeftOutChapterIds.ShouldBeEmpty();
    }
}
