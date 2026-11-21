namespace SmartSolutionsLab.PharmaTraining.BuildingBlocks.Mail;

/// <summary>A plain-text mail to one address. Stub for #9's red tests (T6): not guarded yet.</summary>
public sealed record OutgoingMail(string To, string Subject, string Text);
