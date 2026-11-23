using System.Text.Json;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Domain.CourseVersions;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Tests.ContentReviewing;

/// <summary>A checked review becomes its DTO in one place: chapter ids as Guids, kinds under their own names.</summary>
public class ContentReviewMappingsTests
{
    public static TheoryData<ContentHintKind> Kinds
    {
        get
        {
            var data = new TheoryData<ContentHintKind>();
            foreach (var kind in Enum.GetValues<ContentHintKind>())
            {
                data.Add(kind);
            }

            return data;
        }
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Every_hint_kind_maps_to_its_dto_kind(ContentHintKind kind) =>
        kind.ToDto().ToString().ShouldBe(kind.ToString());

    [Fact]
    public void An_undefined_hint_kind_is_a_bug() =>
        Should.Throw<System.Diagnostics.UnreachableException>(() => ((ContentHintKind)99).ToDto());

    [Fact]
    public void A_review_maps_to_its_reviewed_chapter_ids_and_hints()
    {
        var first = ChapterId.New();
        var second = ChapterId.New();
        var review = new ContentReview([first, second], [new ContentHint(first, ContentHintKind.Gap, "Schritt 3 fehlt."), new ContentHint(second, ContentHintKind.Readability, "Der Satz ist zu lang.")]);

        var dto = review.ToDto();

        dto.ReviewedChapterIds.ShouldBe([first.Value, second.Value]);
        dto.Hints.ShouldBe(
        [
            new EditorialContentHintDto(first.Value, EditorialContentHintKind.Gap, "Schritt 3 fehlt."),
            new EditorialContentHintDto(second.Value, EditorialContentHintKind.Readability, "Der Satz ist zu lang."),
        ]);
    }

    [Fact]
    public void A_hint_kind_travels_by_its_name_in_json() =>
        JsonSerializer.Serialize(EditorialContentHintKind.Contradiction).ShouldBe("\"Contradiction\"");
}
