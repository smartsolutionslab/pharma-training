namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>
/// A course assigned in a pharmacy to some of its employees, due a number of days after the seed's day (0: today, in
/// Germany), optionally recurring, and what each learner then does on the given version of it.
/// </summary>
// stub for #457
public sealed record DemoScenario(
    string Pharmacy,
    IReadOnlyList<string> Learners,
    string Course,
    int CourseVersion,
    int DueInDays,
    int? RecurrenceMonths,
    DemoOutcome Outcome);
