namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.ContentReviewing;

/// <summary>Why the model's answer to an AI content review could not be used; carried by <see cref="AiReviewMalformed"/>, never shown to the caller.</summary>
public enum ContentReviewDefect
{
    /// <summary>Not JSON of the answer's shape: empty, cut off, a null, a missing property or an unknown kind.</summary>
    Unreadable,

    /// <summary>A hint names a chapter number that was not sent.</summary>
    UnknownChapter,

    /// <summary>A hint's text is blank, too long or holds a nul character.</summary>
    InvalidHintText,

    /// <summary>A chapter has more hints than the instruction allows.</summary>
    TooManyHints,
}
