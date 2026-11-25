namespace SmartSolutionsLab.PharmaTraining.DemoSeeder.Tests;

/// <summary>
/// Small plans for the unit tests, built from the same records as the demo plan: three editors, one pharmacy with a manager
/// and five employees, one course of two text chapters with a question each (the plan lists the correct option first),
/// optionally with a second version adding a chapter, and the scenarios a test names.
/// </summary>
internal static class Plans
{
    public const string Author = "demo-autorin";

    public const string Approver = "demo-pruefer";

    public const string Publisher = "demo-freigabe";

    public const string Manager = "demo-test-leitung";

    public const string Pharmacy = "Demo-Apotheke Test";

    public const string Course = "Demo: Testkurs";

    public static readonly string[] Employees = ["demo-test-1", "demo-test-2", "demo-test-3", "demo-test-4", "demo-test-5"];

    public static SeedPlan OnePharmacyOneCourse(params DemoScenario[] scenarios) => Plan(secondVersion: false, scenarios);

    public static SeedPlan WithSecondVersion(params DemoScenario[] scenarios) => Plan(secondVersion: true, scenarios);

    /// <summary>An employee's course, due in <paramref name="dueInDays"/> days, and what they do on its version.</summary>
    public static DemoScenario Scenario(string learner, DemoOutcome outcome, int dueInDays = 30, int version = 1, int? recurrenceMonths = null) =>
        new(Pharmacy, [learner], Course, version, dueInDays, recurrenceMonths, outcome);

    public static DemoChapter Chapter(int number) => new(
        $"Kapitel {number}",
        $"Text des Kapitels {number}.",
        [new DemoQuestion($"Frage zu Kapitel {number}?", [new DemoOption($"Richtig {number}", true), new DemoOption($"Falsch {number}a", false), new DemoOption($"Falsch {number}b", false)])]);

    private static SeedPlan Plan(bool secondVersion, DemoScenario[] scenarios) => new(
        new DemoEditors(Author, Approver, Publisher),
        [
            new DemoUser(Author, "Petra", "Autorin", $"{Author}@example.com", "editor"),
            new DemoUser(Approver, "Jonas", "Prüfer", $"{Approver}@example.com", "editor"),
            new DemoUser(Publisher, "Lena", "Freigabe", $"{Publisher}@example.com", "editor"),
            new DemoUser(Manager, "Andrea", "Brandt", $"{Manager}@example.com", null),
            .. Employees.Select((employee, index) => new DemoUser(employee, "Person", $"Nummer {index + 1}", $"{employee}@example.com", null)),
        ],
        [new DemoPharmacy(Pharmacy, Manager, [.. Employees.Select(employee => new DemoEmployee(employee, DemoJobFunction.Pta))])],
        [new DemoCourse(Course, [Chapter(1), Chapter(2)], new DemoBlueprint(2, 80, 12), secondVersion ? [Chapter(3)] : [])],
        scenarios);
}
