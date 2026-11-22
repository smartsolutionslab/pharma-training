namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api.Endpoints;

/// <summary>The body that asks for an upload URL: the kind of chapter the file is for, Pdf or Video.</summary>
public sealed record EditorialUploadRequest(EditorialChapterRequestKind? Kind);
