namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>Who writes and submits, who approves the questions, and who publishes: three people, so four eyes holds.</summary>
public sealed record DemoEditors(string Author, string Approver, string Publisher);
