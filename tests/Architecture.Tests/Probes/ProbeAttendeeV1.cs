namespace SmartSolutionsLab.PharmaTraining.Architecture.Tests.Probes;

/// <summary>A record inside an event that carries a name, so the rule must look into it.</summary>
public sealed record ProbeAttendeeV1(Guid PersonId, string HolderName);
