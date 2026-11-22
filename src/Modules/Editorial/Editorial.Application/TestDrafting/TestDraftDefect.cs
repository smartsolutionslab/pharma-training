namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.TestDrafting;

/// <summary>Why the model's answer to an AI test draft could not be used; carried by <see cref="AiTestDraftMalformed"/>, never shown to the caller.</summary>
public enum TestDraftDefect
{
    /// <summary>Not JSON of the answer's shape: empty, cut off, a null, a missing property or an unknown question type.</summary>
    Unreadable,

    /// <summary>More or fewer questions than were asked for.</summary>
    WrongCount,

    /// <summary>A question names a chapter number that was not sent.</summary>
    UnknownChapter,

    /// <summary>A question's text is blank, too long or holds a nul character.</summary>
    InvalidQuestionText,

    /// <summary>An option's text is blank, too long or holds a nul character.</summary>
    InvalidOptionText,

    /// <summary>A question has more options than a drafted question may hold.</summary>
    TooManyOptions,
}
