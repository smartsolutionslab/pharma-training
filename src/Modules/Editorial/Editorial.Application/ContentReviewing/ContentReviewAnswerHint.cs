namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;

/// <summary>One hint as the model wrote it, naming its chapter by number: untrusted until checked.</summary>
public sealed record ContentReviewAnswerHint(int Chapter, ContentReviewAnswerKind Kind, string Text);
