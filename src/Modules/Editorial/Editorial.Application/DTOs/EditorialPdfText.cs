using System.Text.Json.Serialization;

namespace SmartSolutionsLab.PharmaTraining.Modules.Editorial.Application.DTOs;

/// <summary>
/// Whether a PDF chapter has text to draft test questions from (#157): extracted whole, cut at a limit, or none (a scan,
/// an unreadable file, or a PDF recorded before extraction existed); travels by name.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<EditorialPdfText>))]
public enum EditorialPdfText
{
    Complete,
    Truncated,
    NoText,
}
