using Microsoft.Extensions.Time.Testing;
using SmartSolutionsLab.PharmaTraining.BuildingBlocks.EventSourcing;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.AnswerQuestion;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.StartAttempt;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Commands.SubmitAttempt;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.DTOs;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Queries.GetAttemptResult;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Queries.GetCurrentAttempt;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.Queries.GetExaminationBySession;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Application.ReadModels;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Domain.Examinations;
using SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Consumers;

namespace SmartSolutionsLab.PharmaTraining.Modules.Assessment.Tests.Attempts;

/// <summary>
/// One learner's examination of one learning session on version V, and the use cases that work on it, over the
/// module's streams, its copy of V's test and its outbox in memory. V's test passes at 80 % and allows three attempts:
/// Q1 is single choice in chapter Hygiene with A right and D wrong; Q2 is multiple choice in chapter Gloves with B and
/// C right and E wrong. Two questions, so one wrong answer scores 50 %.
/// </summary>
internal sealed class AttemptFixture
{
    public const string Q1Text = "Ist Händewaschen vor dem Herstellen Pflicht?";
    public const string Q2Text = "Was gehört zur Schutzkleidung?";

    public static readonly DateTimeOffset Now = new(2027, 3, 1, 9, 0, 0, TimeSpan.Zero);

    public static readonly ChapterId Hygiene = ChapterId.From(Guid.CreateVersion7());
    public static readonly ChapterId Gloves = ChapterId.From(Guid.CreateVersion7());
    public static readonly QuestionVersionId Q1 = QuestionVersionId.From(Guid.CreateVersion7());
    public static readonly QuestionVersionId Q2 = QuestionVersionId.From(Guid.CreateVersion7());
    public static readonly AnswerOptionId A = NewOption();
    public static readonly AnswerOptionId D = NewOption();
    public static readonly AnswerOptionId B = NewOption();
    public static readonly AnswerOptionId C = NewOption();
    public static readonly AnswerOptionId E = NewOption();

    public AttemptFixture()
    {
        Store = new InMemoryAssessmentAggregateStore(Steps);
        UnitOfWork = new RecordingAssessmentUnitOfWork(Steps);
    }

    public List<string> Steps { get; } = [];

    public FakeTimeProvider Time { get; } = new(Now);

    public InMemoryAssessmentAggregateStore Store { get; }

    public InMemoryTestDefinitionCopies Copies { get; } = new();

    public RecordingAssessmentUnitOfWork UnitOfWork { get; }

    /// <summary>The saves since the examination was opened or last changed outside the use cases.</summary>
    public int Saves => Steps.Count(step => step == InMemoryAssessmentAggregateStore.Saved);

    public CourseId Course { get; } = CourseId.From(Guid.CreateVersion7());

    public CourseVersionId Version { get; } = CourseVersionId.From(Guid.CreateVersion7());

    public LearningSessionId Session { get; } = LearningSessionId.From(Guid.CreateVersion7());

    public PersonId Learner { get; } = PersonId.From(Guid.CreateVersion7());

    public ExaminationId Examination => ExaminationId.For(Session);

    public static CancellationToken Token => TestContext.Current.CancellationToken;

    /// <summary>V's test as the domain scores it, written out by hand: arranging a passed or exhausted examination needs no mapping of the copy.</summary>
    public TestDefinition Definition =>
        new(
            Version,
            [
                new DefinedQuestion(Q1, Hygiene, [A, D], [A]),
                new DefinedQuestion(Q2, Gloves, [B, C, E], [B, C]),
            ],
            ScorePercent.From(80),
            AttemptNumber.From(3));

    /// <summary>V's test as Assessment copied it from Editorial, solutions included; its lists out of position order, as a store may hold them.</summary>
    public TestDefinitionCopy Copy() =>
        new()
        {
            CourseVersionId = Version.Value,
            CourseId = Course.Value,
            PassingScorePercent = 80,
            MaxAttempts = 3,
            Questions =
            [
                new TestQuestionCopy
                {
                    CourseVersionId = Version.Value,
                    QuestionId = Q2.Value,
                    Position = 2,
                    ChapterId = Gloves.Value,
                    Type = TestQuestionType.MultipleChoice,
                    Text = Q2Text,
                    Options =
                    [
                        OptionCopy(Q2, E, 3, "Schmuck", isCorrect: false),
                        OptionCopy(Q2, B, 1, "Handschuhe", isCorrect: true),
                        OptionCopy(Q2, C, 2, "Kittel", isCorrect: true),
                    ],
                },
                new TestQuestionCopy
                {
                    CourseVersionId = Version.Value,
                    QuestionId = Q1.Value,
                    Position = 1,
                    ChapterId = Hygiene.Value,
                    Type = TestQuestionType.SingleChoice,
                    Text = Q1Text,
                    Options =
                    [
                        OptionCopy(Q1, D, 2, "Nein", isCorrect: false),
                        OptionCopy(Q1, A, 1, "Ja", isCorrect: true),
                    ],
                },
            ],
        };

    public static string TextOf(AnswerOptionId option) =>
        option == A ? "Ja" : option == D ? "Nein" : option == B ? "Handschuhe" : option == C ? "Kittel" : "Schmuck";

    /// <summary>The examination opened for the learner, as ContentCompletedV1 opens it, with V's copy stored unless it has not arrived yet; the steps start counting after it.</summary>
    public async Task OpenAsync(bool withDefinition = true)
    {
        if (withDefinition)
        {
            Copies.Definitions.Add(Copy());
        }

        await Store.SaveAsync(Domain.Examinations.Examination.Open(Examination, Session, Learner, Course, Version, Timestamp.From(Now)), Token);
        Steps.Clear();
    }

    /// <summary>Stores what the domain does to the examination outside the use cases under test, such as a whole earlier attempt.</summary>
    public async Task ChangeAsync(Action<Examination> change)
    {
        var examination = (await Store.LoadAsync<Examination>(Examination, Token)).Value;
        change(examination);
        await Store.SaveAsync(examination, Token);
        Steps.Clear();
    }

    /// <summary>A whole attempt through the domain: started in the copy's order, each question answered as given, submitted.</summary>
    public Task AttemptThroughTheDomainAsync(IReadOnlyList<AnswerOptionId> q1, IReadOnlyList<AnswerOptionId> q2) =>
        ChangeAsync(examination =>
        {
            examination.StartAttempt(AttemptId.New(), [Q1, Q2], Timestamp.From(Now)).IsSuccess.ShouldBeTrue();
            examination.Answer(Q1, q1, Timestamp.From(Now)).IsSuccess.ShouldBeTrue();
            examination.Answer(Q2, q2, Timestamp.From(Now)).IsSuccess.ShouldBeTrue();
            examination.Submit(Definition, Timestamp.From(Now)).IsSuccess.ShouldBeTrue();
        });

    public IReadOnlyList<IDomainEvent> Stream() => Store.Streams[Examination.Value];

    public Task<Result<AttemptId, ApiError>> StartAsync(PersonId? by = null, ExaminationId? examination = null) =>
        new StartAttemptHandler(Store, Copies, Time).HandleAsync(new StartAttempt(examination ?? Examination, by ?? Learner), Token);

    public Task<Result<Unit, ApiError>> AnswerAsync(QuestionVersionId question, params AnswerOptionId[] chosen) =>
        AnswerAsync(Learner, question, chosen);

    public Task<Result<Unit, ApiError>> AnswerAsync(PersonId by, QuestionVersionId question, params AnswerOptionId[] chosen) =>
        new AnswerQuestionHandler(Store, Copies, Time).HandleAsync(new AnswerQuestion(Examination, by, question, chosen), Token);

    public Task<Result<AttemptId, ApiError>> SubmitAsync(PersonId? by = null) =>
        new SubmitAttemptHandler(Store, Copies, UnitOfWork, Time).HandleAsync(new SubmitAttempt(Examination, by ?? Learner), Token);

    public Task<Result<AssessmentExaminationDto, ApiError>> BySessionAsync(Guid? session = null, Guid? by = null) =>
        new GetExaminationBySessionHandler(Store, Copies).HandleAsync(new GetExaminationBySession(session ?? Session.Value, by ?? Learner.Value), Token);

    public Task<Result<AssessmentAttemptDto, ApiError>> CurrentAttemptAsync(Guid? by = null) =>
        new GetCurrentAttemptHandler(Store, Copies).HandleAsync(new GetCurrentAttempt(Examination.Value, by ?? Learner.Value), Token);

    public Task<Result<AssessmentAttemptResultDto, ApiError>> ResultAsync(AttemptId attempt, Guid? by = null) =>
        new GetAttemptResultHandler(Store, Copies).HandleAsync(new GetAttemptResult(Examination.Value, attempt.Value, by ?? Learner.Value), Token);

    /// <summary>Starts an attempt, answers Q1 and Q2 as given and submits it, all through the use cases; the submit's result.</summary>
    public async Task<Result<AttemptId, ApiError>> AttemptAsync(AnswerOptionId[] q1, AnswerOptionId[] q2)
    {
        (await StartAsync()).IsSuccess.ShouldBeTrue();
        (await AnswerAsync(Q1, q1)).IsSuccess.ShouldBeTrue();
        (await AnswerAsync(Q2, q2)).IsSuccess.ShouldBeTrue();
        return await SubmitAsync();
    }

    private static AnswerOptionId NewOption() => AnswerOptionId.From(Guid.CreateVersion7());

    private TestOptionCopy OptionCopy(QuestionVersionId question, AnswerOptionId option, int position, string text, bool isCorrect) =>
        new()
        {
            CourseVersionId = Version.Value,
            QuestionId = question.Value,
            OptionId = option.Value,
            Position = position,
            Text = text,
            IsCorrect = isCorrect,
        };
}
