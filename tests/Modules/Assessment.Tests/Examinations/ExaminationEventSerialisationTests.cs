using System.Text.Json;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventStore;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations.Events;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Examinations;

/// <summary>
/// Every examination event is stored forever and read back by Marten through the options ModuleEventStore.Configure
/// gives it: each must come back equal, its lists element by element.
/// </summary>
public class ExaminationEventSerialisationTests
{
    private static readonly JsonSerializerOptions EventStore = CreateEventStoreOptions();

    private static readonly Timestamp At = Timestamp.From(new DateTimeOffset(2027, 3, 1, 9, 0, 0, TimeSpan.Zero));
    private static readonly ExaminationId Examination = ExaminationId.New();
    private static readonly LearningSessionId Session = LearningSessionId.From(Guid.CreateVersion7());
    private static readonly PersonId Learner = PersonId.From(Guid.CreateVersion7());
    private static readonly CourseId Course = CourseId.From(Guid.CreateVersion7());
    private static readonly CourseVersionId Version = CourseVersionId.From(Guid.CreateVersion7());
    private static readonly AttemptId Attempt = AttemptId.New();

    [Theory]
    [InlineData(nameof(ExaminationOpened))]
    [InlineData(nameof(ExaminationUnlocked))]
    [InlineData(nameof(AttemptStarted))]
    [InlineData(nameof(QuestionAnswered))]
    [InlineData(nameof(AttemptSubmitted))]
    [InlineData(nameof(ExaminationPassed))]
    [InlineData(nameof(ExaminationExhausted))]
    public void Every_examination_event_survives_the_event_stores_serialisation(string name)
    {
        var stored = Event(name);

        var json = JsonSerializer.Serialize(stored, stored.GetType(), EventStore);
        var read = (IDomainEvent)JsonSerializer.Deserialize(json, stored.GetType(), EventStore)!;

        read.ShouldBeOfType(stored.GetType());
        WithStoredLists(read, stored).ShouldBe(stored, json);
    }

    private static IDomainEvent Event(string name) =>
        name switch
        {
            nameof(ExaminationOpened) => new ExaminationOpened(Examination, Session, Learner, Course, Version, At),
            nameof(ExaminationUnlocked) => new ExaminationUnlocked(Examination, At),
            nameof(AttemptStarted) => new AttemptStarted(Examination, Attempt, AttemptNumber.From(2), [NewQuestion(), NewQuestion()], At),
            nameof(QuestionAnswered) => new QuestionAnswered(Examination, Attempt, NewQuestion(), [NewOption(), NewOption()], At),
            nameof(AttemptSubmitted) => new AttemptSubmitted(Examination, Attempt, ScorePercent.From(66), AttemptOutcome.Failed, [NewChapter(), NewChapter()], At),
            nameof(ExaminationPassed) => new ExaminationPassed(Examination, Session, Learner, Course, Version, Attempt, ScorePercent.From(100), At),
            nameof(ExaminationExhausted) => new ExaminationExhausted(Examination, Session, Learner, Version, [NewChapter(), NewChapter()], At),
            _ => throw new InvalidOperationException($"{name} is no examination event."),
        };

    /// <summary>
    /// A record compares a list by reference: each list read back is compared element by element, then replaced by the
    /// stored one, so the record's own equality compares every other member.
    /// </summary>
    private static IDomainEvent WithStoredLists(IDomainEvent read, IDomainEvent stored) =>
        (read, stored) switch
        {
            (AttemptStarted started, AttemptStarted original) => Same(started.Questions, original.Questions, started with { Questions = original.Questions }),
            (QuestionAnswered answered, QuestionAnswered original) => Same(answered.Chosen, original.Chosen, answered with { Chosen = original.Chosen }),
            (AttemptSubmitted submitted, AttemptSubmitted original) => Same(submitted.FailedChapters, original.FailedChapters, submitted with { FailedChapters = original.FailedChapters }),
            (ExaminationExhausted exhausted, ExaminationExhausted original) => Same(exhausted.FailedChapters, original.FailedChapters, exhausted with { FailedChapters = original.FailedChapters }),
            _ => read,
        };

    private static IDomainEvent Same<T>(IReadOnlyList<T> read, IReadOnlyList<T> stored, IDomainEvent withStoredList)
    {
        read.ShouldBe(stored);
        return withStoredList;
    }

    private static JsonSerializerOptions CreateEventStoreOptions()
    {
        var options = new JsonSerializerOptions
        {
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
        };
        options.Converters.Add(new ValueObjectJsonConverterFactory());
        return options;
    }

    private static QuestionVersionId NewQuestion() => QuestionVersionId.From(Guid.CreateVersion7());

    private static AnswerOptionId NewOption() => AnswerOptionId.From(Guid.CreateVersion7());

    private static ChapterId NewChapter() => ChapterId.From(Guid.CreateVersion7());
}
