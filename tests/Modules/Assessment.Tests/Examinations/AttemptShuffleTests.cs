using System.Globalization;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Examinations;

/// <summary>
/// An attempt's order is derived from its id (ADR 0022): each item's key is SHA-256 of the attempt's Guid and the
/// item's Guid, both big-endian, and the items are ordered by their keys' bytes. The vectors were computed with
/// Python's hashlib, independently of the C# code; they may never change, or every stored attempt's options reorder.
/// </summary>
public class AttemptShuffleTests
{
    private static readonly AttemptId Pinned = AttemptId.From(Guid.Parse("0192f5d4-7c3a-7b2e-9a41-5e6f7d8c9b0a"));
    private static readonly AttemptId Neighbour = AttemptId.From(Guid.Parse("0192f5d4-7c3a-7b2e-9a41-5e6f7d8c9b0b"));

    [Fact]
    public void The_order_of_a_known_attempt_is_pinned()
    {
        AttemptShuffle.Questions(Pinned, Numbered(QuestionVersionId.From, 1, 2, 3, 4, 5))
            .ShouldBe(Numbered(QuestionVersionId.From, 2, 5, 4, 3, 1));
        AttemptShuffle.Options(Pinned, Numbered(AnswerOptionId.From, 1, 2, 3, 4, 5))
            .ShouldBe(Numbered(AnswerOptionId.From, 2, 5, 4, 3, 1));
    }

    [Fact]
    public void Another_attempt_orders_the_same_items_differently()
    {
        AttemptShuffle.Questions(Neighbour, Numbered(QuestionVersionId.From, 1, 2, 3, 4, 5))
            .ShouldBe(Numbered(QuestionVersionId.From, 1, 4, 3, 5, 2));
        AttemptShuffle.Options(Neighbour, Numbered(AnswerOptionId.From, 1, 2, 3, 4, 5))
            .ShouldBe(Numbered(AnswerOptionId.From, 1, 4, 3, 5, 2));
    }

    [Fact]
    public void The_order_does_not_depend_on_the_input_order()
    {
        var items = Numbered(QuestionVersionId.From, 1, 2, 3, 4, 5);
        List<QuestionVersionId> reversed = [.. items];
        reversed.Reverse();

        AttemptShuffle.Questions(Pinned, reversed).ShouldBe(AttemptShuffle.Questions(Pinned, items));
        AttemptShuffle.Questions(Pinned, reversed).ShouldBe(Numbered(QuestionVersionId.From, 2, 5, 4, 3, 1));
    }

    [Fact]
    public void The_order_is_a_permutation_of_the_items()
    {
        List<AnswerOptionId> items = [.. Enumerable.Range(0, 10).Select(_ => AnswerOptionId.From(Guid.CreateVersion7()))];
        var attempt = AttemptId.New();

        var ordered = AttemptShuffle.Options(attempt, items);

        ordered.Count.ShouldBe(10);
        ordered.Distinct().Count().ShouldBe(10);
        ordered.ShouldBe(items, ignoreOrder: true);
        AttemptShuffle.Options(attempt, items).ShouldBe(ordered);
    }

    [Fact]
    public void Duplicate_items_are_refused()
    {
        var question = QuestionVersionId.From(Guid.CreateVersion7());
        var option = AnswerOptionId.From(Guid.CreateVersion7());

        Should.Throw<GuardException>(() => AttemptShuffle.Questions(Pinned, [question, QuestionVersionId.From(Guid.CreateVersion7()), question]));
        Should.Throw<GuardException>(() => AttemptShuffle.Options(Pinned, [option, option]));
    }

    /// <summary>The items 00000000-0000-7000-8000-00000000000N for each N, in the order given.</summary>
    private static List<T> Numbered<T>(Func<Guid, T> from, params int[] numbers)
        where T : notnull =>
        [.. numbers.Select(number => from(Guid.Parse("00000000-0000-7000-8000-" + number.ToString("D12", CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)))];
}
