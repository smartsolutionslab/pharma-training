namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>What the seeder makes, as data: the users, the pharmacies, the courses and the scenarios driven on them.</summary>
public sealed record SeedPlan(
    DemoEditors Editors,
    IReadOnlyList<DemoUser> Users,
    IReadOnlyList<DemoPharmacy> Pharmacies,
    IReadOnlyList<DemoCourse> Courses,
    IReadOnlyList<DemoScenario> Scenarios)
{
    private const string Markt = "Demo-Apotheke am Markt";

    private const string Rosen = "Demo-Apotheke Rosenstraße";

    private const string Loewen = "Demo-Löwen-Apotheke Großstadt";

    private const string Hygiene = "Demo: Hygiene in der Apotheke";

    private const string Btm = "Demo: BtM-Dokumentation";

    private const string Dsgvo = "Demo: Datenschutz (DSGVO) im Apothekenalltag";

    private const string FirstAid = "Demo: Erste Hilfe / Notfallmanagement";

    /// <summary>The demo data of #457: three pharmacies, four courses and a second Hygiene version, and every assignment state.</summary>
    public static SeedPlan Demo { get; } = CreateDemo();

    private static SeedPlan CreateDemo()
    {
        var editors = new DemoEditors("demo-autorin", "demo-pruefer", "demo-freigabe");
        (string Username, string FirstName, string LastName, DemoJobFunction JobFunction)[] markt =
        [
            ("demo-markt-1", "Jana", "Weber", DemoJobFunction.Pta),
            ("demo-markt-2", "Tim", "Krause", DemoJobFunction.Pka),
            ("demo-markt-3", "Eva", "Schulz", DemoJobFunction.Pharmacist),
            ("demo-markt-4", "Leon", "Hoffmann", DemoJobFunction.Trainee),
        ];
        (string Username, string FirstName, string LastName, DemoJobFunction JobFunction)[] rosen =
        [
            ("demo-rosen-1", "Klara", "Becker", DemoJobFunction.Pta),
            ("demo-rosen-2", "Felix", "Wagner", DemoJobFunction.Pka),
            ("demo-rosen-3", "Marie", "Fischer", DemoJobFunction.Pharmacist),
        ];
        (string Username, string FirstName, string LastName, DemoJobFunction JobFunction)[] loewen =
        [
            ("demo-loewen-01", "Anna", "Schmidt", DemoJobFunction.Pharmacist),
            ("demo-loewen-02", "Lukas", "Meyer", DemoJobFunction.Pta),
            ("demo-loewen-03", "Sophie", "Wolf", DemoJobFunction.Pta),
            ("demo-loewen-04", "Paul", "Neumann", DemoJobFunction.Pka),
            ("demo-loewen-05", "Laura", "Schwarz", DemoJobFunction.Pta),
            ("demo-loewen-06", "Max", "Zimmermann", DemoJobFunction.Pharmacist),
            ("demo-loewen-07", "Lea", "Braun", DemoJobFunction.Pka),
            ("demo-loewen-08", "Ben", "Hofmann", DemoJobFunction.Pta),
            ("demo-loewen-09", "Mia", "Hartmann", DemoJobFunction.Trainee),
            ("demo-loewen-10", "Elias", "Lange", DemoJobFunction.Pta),
            ("demo-loewen-11", "Emma", "Schmitt", DemoJobFunction.Pka),
            ("demo-loewen-12", "Noah", "Werner", DemoJobFunction.Pharmacist),
            ("demo-loewen-13", "Hannah", "Krüger", DemoJobFunction.Pta),
            ("demo-loewen-14", "Finn", "Lehmann", DemoJobFunction.Other),
            ("demo-loewen-15", "Lina", "Köhler", DemoJobFunction.Pta),
            ("demo-loewen-16", "Jan", "Herrmann", DemoJobFunction.Pka),
            ("demo-loewen-17", "Lena", "König", DemoJobFunction.Pta),
            ("demo-loewen-18", "David", "Walter", DemoJobFunction.Trainee),
            ("demo-loewen-19", "Julia", "Mayer", DemoJobFunction.Pta),
            ("demo-loewen-20", "Moritz", "Huber", DemoJobFunction.Pharmacist),
            ("demo-loewen-21", "Clara", "Kaiser", DemoJobFunction.Pka),
            ("demo-loewen-22", "Simon", "Fuchs", DemoJobFunction.Pta),
        ];

        List<DemoUser> users =
        [
            User(editors.Author, "Petra", "Autorin", "editor"),
            User(editors.Approver, "Jonas", "Prüfer", "editor"),
            User(editors.Publisher, "Lena", "Freigabe", "editor"),
            User("demo-markt-leitung", "Andrea", "Brandt"),
            User("demo-rosen-leitung", "Michael", "Roth"),
            User("demo-loewen-leitung", "Sabine", "Löwe"),
            .. markt.Concat(rosen).Concat(loewen).Select(employee => User(employee.Username, employee.FirstName, employee.LastName)),
        ];

        DemoPharmacy[] pharmacies =
        [
            new(Markt, "demo-markt-leitung", [.. markt.Select(employee => new DemoEmployee(employee.Username, employee.JobFunction))]),
            new(Rosen, "demo-rosen-leitung", [.. rosen.Select(employee => new DemoEmployee(employee.Username, employee.JobFunction))]),
            new(Loewen, "demo-loewen-leitung", [.. loewen.Select(employee => new DemoEmployee(employee.Username, employee.JobFunction))]),
        ];

        string[] loewenAll = [.. loewen.Select(employee => employee.Username)];
        DemoScenario[] scenarios =
        [
            // Markt: every assignment state, a recurrence, and Hygiene passed on its first version before the second supersedes it.
            new(Markt, ["demo-markt-1", "demo-markt-2"], Hygiene, 1, 30, null, DemoOutcome.Passed),
            new(Markt, ["demo-markt-1"], Dsgvo, 1, 30, 12, DemoOutcome.Passed),
            new(Markt, ["demo-markt-3"], Btm, 1, 14, null, DemoOutcome.Started),
            new(Markt, ["demo-markt-4"], FirstAid, 1, 0, null, DemoOutcome.Open),
            new(Markt, ["demo-markt-2", "demo-markt-3", "demo-markt-4"], Dsgvo, 1, 60, null, DemoOutcome.Open),

            // Rosen: a pass, a failed attempt, an exhausted examination, and everyone due today, overdue from tomorrow (gap G1).
            new(Rosen, ["demo-rosen-1"], Btm, 1, 30, null, DemoOutcome.Passed),
            new(Rosen, ["demo-rosen-2"], Hygiene, 2, 30, null, DemoOutcome.Failed),
            new(Rosen, ["demo-rosen-3"], Dsgvo, 1, 30, null, DemoOutcome.Exhausted),
            new(Rosen, ["demo-rosen-1", "demo-rosen-2", "demo-rosen-3"], FirstAid, 1, 0, null, DemoOutcome.Open),

            // Löwen: 22 employees, so its members and its matrix have two pages at the default page size.
            new(Loewen, loewenAll[..6], Hygiene, 2, 45, null, DemoOutcome.Passed),
            new(Loewen, loewenAll[6..10], Hygiene, 2, 45, null, DemoOutcome.Started),
            new(Loewen, loewenAll[10..], Hygiene, 2, 45, null, DemoOutcome.Open),
            new(Loewen, loewenAll, FirstAid, 1, 90, null, DemoOutcome.Open),
        ];

        return new SeedPlan(editors, users, pharmacies, DemoCourses(), scenarios);
    }

    private static DemoUser User(string username, string firstName, string lastName, string? realmRole = null) =>
        new(username, firstName, lastName, $"{username}@example.com", realmRole);

    private static DemoQuestion Question(string text, string correct, string wrong, string alsoWrong) =>
        new(text, [new DemoOption(correct, true), new DemoOption(wrong, false), new DemoOption(alsoWrong, false)]);

    private static DemoCourse[] DemoCourses() =>
    [
        new(
            Hygiene,
            [
                new("Händehygiene", "Die hygienische Händedesinfektion ist die wichtigste Einzelmaßnahme gegen die Übertragung von Erregern. Das Mittel wird mindestens 30 Sekunden lang auf allen Flächen der Hände verrieben.", [
                    Question("Wie lange dauert eine hygienische Händedesinfektion mindestens?", "30 Sekunden", "5 Sekunden", "5 Minuten"),
                    Question("Wann ist eine Händedesinfektion vorgeschrieben?", "Vor dem Herstellen einer Rezeptur", "Nur nach Dienstschluss", "Nur bei sichtbarer Verschmutzung"),
                ]),
                new("Flächendesinfektion in der Rezeptur", "Arbeitsflächen in der Rezeptur werden vor und nach jeder Herstellung mit einem gelisteten Flächendesinfektionsmittel behandelt; die Einwirkzeit wird eingehalten.", [
                    Question("Womit werden Arbeitsflächen in der Rezeptur desinfiziert?", "Mit einem gelisteten Flächendesinfektionsmittel", "Mit Leitungswasser", "Mit Glasreiniger"),
                ]),
                new("Der Hygieneplan", "Der Hygieneplan legt fest, wer was wann womit reinigt und desinfiziert. Alle Mitarbeitenden kennen ihn und bestätigen die Unterweisung.", [
                    Question("Wer muss den Hygieneplan der Apotheke kennen?", "Alle Mitarbeitenden", "Nur die Apothekenleitung", "Nur das Reinigungspersonal"),
                ]),
            ],
            new DemoBlueprint(4, 80, 12),
            [
                new("Hygiene im Kundenkontakt", "Bei Kundinnen und Kunden mit Atemwegsinfekten wird Abstand gehalten; nach dem Kontakt werden die Hände desinfiziert.", [
                    Question("Was gilt im Kontakt mit Kundschaft mit Atemwegsinfekt?", "Abstand halten und danach die Hände desinfizieren", "Nichts Besonderes", "Die Apotheke sofort schließen"),
                ]),
            ]),
        new(
            Btm,
            [
                new("Grundlagen des BtM-Verkehrs", "Der Verkehr mit Betäubungsmitteln ist im Betäubungsmittelgesetz und in der Betäubungsmittel-Verschreibungsverordnung geregelt.", [
                    Question("Welches Gesetz regelt den Verkehr mit Betäubungsmitteln?", "Das Betäubungsmittelgesetz (BtMG)", "Allein das Ladenschlussgesetz", "Allein die Gewerbeordnung"),
                ]),
                new("Das BtM-Rezept", "Ein BtM-Rezept wird innerhalb von sieben Tagen nach seiner Ausstellung beliefert. Teil I verbleibt in der Apotheke, Teil II dient der Abrechnung.", [
                    Question("Wie lange darf ein BtM-Rezept nach seiner Ausstellung beliefert werden?", "Sieben Tage", "Drei Monate", "Ein Jahr"),
                    Question("Welcher Teil des BtM-Rezepts verbleibt in der Apotheke?", "Teil I", "Teil III", "Keiner der Teile"),
                ]),
                new("Dokumentation und Aufbewahrung", "Zu- und Abgänge werden lückenlos dokumentiert; die Nachweise werden drei Jahre aufbewahrt.", [
                    Question("Wie lange werden die BtM-Nachweise aufbewahrt?", "Drei Jahre", "Drei Monate", "Dreißig Jahre"),
                ]),
            ],
            new DemoBlueprint(4, 80, 12),
            []),
        new(
            Dsgvo,
            [
                new("Gesundheitsdaten", "Gesundheitsdaten gehören zu den besonderen Kategorien personenbezogener Daten und sind besonders geschützt.", [
                    Question("Was sind Gesundheitsdaten nach der DSGVO?", "Besondere Kategorien personenbezogener Daten", "Öffentlich zugängliche Daten", "Anonyme Statistikdaten"),
                ]),
                new("Diskretion am HV-Tisch", "Am HV-Tisch wird mit Diskretionsabstand und leise beraten. Auskünfte über die Medikation Dritter gibt es nur mit deren Einwilligung.", [
                    Question("Wie wahren Sie Diskretion am HV-Tisch?", "Diskretionsabstand einhalten und leise beraten", "Die Medikation laut wiederholen", "Rezepte offen liegen lassen"),
                    Question("Darf jemand ohne Einwilligung Auskunft über die Medikation eines Nachbarn erhalten?", "Nein", "Ja, immer", "Ja, wenn freundlich gefragt wird"),
                ]),
                new("Datenpannen", "Eine Datenpanne wird sofort der Apothekenleitung oder dem Datenschutzbeauftragten gemeldet; die Meldefrist an die Behörde beträgt 72 Stunden.", [
                    Question("Was tun Sie bei einer Datenpanne zuerst?", "Sofort die Leitung oder den Datenschutzbeauftragten informieren", "Abwarten, ob es jemand bemerkt", "Die Spuren löschen"),
                ]),
            ],
            new DemoBlueprint(4, 80, 12),
            []),
        new(
            FirstAid,
            [
                new("Der Notruf", "Den Rettungsdienst erreichen Sie europaweit unter der 112. Melden Sie, wo, was, wie viele Verletzte, und warten Sie auf Rückfragen.", [
                    Question("Welche Nummer wählen Sie für den Rettungsdienst?", "112", "110", "116 117"),
                ]),
                new("Bewusstlosigkeit", "Eine bewusstlose Person mit normaler Atmung wird in die stabile Seitenlage gebracht und bis zum Eintreffen des Rettungsdienstes beobachtet.", [
                    Question("Wie lagern Sie eine bewusstlose Person mit normaler Atmung?", "In der stabilen Seitenlage", "Auf dem Bauch", "Sitzend an die Wand gelehnt"),
                ]),
                new("Herz-Lungen-Wiederbelebung", "Ohne normale Atmung beginnen Sie sofort mit 30 Herzdruckmassagen und 2 Beatmungen, 5 bis 6 Zentimeter tief und 100- bis 120-mal pro Minute.", [
                    Question("In welchem Verhältnis wechseln Herzdruckmassage und Beatmung?", "30 zu 2", "15 zu 1", "5 zu 5"),
                    Question("Wie tief drücken Sie den Brustkorb eines Erwachsenen ein?", "5 bis 6 Zentimeter", "1 bis 2 Zentimeter", "10 bis 12 Zentimeter"),
                ]),
                new("Notfallausstattung der Apotheke", "Der Notfallkoffer steht an einem bekannten, gut erreichbaren Ort; sein Inhalt wird regelmäßig geprüft.", [
                    Question("Wo gehört der Notfallkoffer der Apotheke hin?", "An einen bekannten, gut erreichbaren Ort", "In den abgeschlossenen Keller", "In das Auto der Leitung"),
                ]),
            ],
            new DemoBlueprint(5, 80, 24),
            []),
    ];
}
