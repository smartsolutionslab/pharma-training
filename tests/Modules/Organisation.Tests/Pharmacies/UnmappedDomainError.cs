namespace SmartSolutionsLab.PharmaTraining.Modules.Organisation.Tests.Pharmacies;

/// <summary>A domain error no Organisation use case knows, to prove that <c>PharmacyFailures.From</c> never guesses a status.</summary>
internal sealed record UnmappedDomainError()
    : DomainError("organisation.test-unmapped", "Kein Anwendungsfall gibt diesen Fehler zurück.");
