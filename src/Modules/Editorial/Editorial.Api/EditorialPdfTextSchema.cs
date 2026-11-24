using System.Text.Json;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Api;

/// <summary>
/// Keeps null out of the values of <see cref="EditorialPdfText"/>'s schema. The generator writes <c>null</c> among the
/// values of an enum it first meets as nullable, as <c>EditorialChapterDto.PdfText</c> is, and every place naming the
/// component would take it; the property's own schema already says it may be null.
/// </summary>
internal static class EditorialPdfTextSchema
{
    public static void Describe(IServiceCollection services) =>
        services.ConfigureAll<OpenApiOptions>(options => options.AddSchemaTransformer((schema, context, cancellationToken) =>
        {
            var type = context.JsonTypeInfo.Type;
            if ((Nullable.GetUnderlyingType(type) ?? type) == typeof(EditorialPdfText) && schema.Enum is { } values)
            {
                for (var index = values.Count - 1; index >= 0; index--)
                {
                    if (values[index] is null || values[index]!.GetValueKind() == JsonValueKind.Null)
                    {
                        values.RemoveAt(index);
                    }
                }
            }

            return Task.CompletedTask;
        }));
}
