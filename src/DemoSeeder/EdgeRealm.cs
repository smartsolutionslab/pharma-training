namespace SmartSolutionsLab.PharmaTraining.DemoSeeder;

/// <summary>The realm the edge's <c>/api/config.json</c> names, accepted by <see cref="SeederSettings.TryAccept"/>: where users sign in, and the web client.</summary>
internal sealed record EdgeRealm(Uri Authority, string ClientId);
