using System.Text.Json.Serialization;

namespace PredictiveAnalysis.Models.Entities;

/// <summary>
/// Entity mapped from mockData/Parts.json. Master catalog of spare parts and lead times.
/// </summary>
public sealed class Part
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("partName")]
    public string PartName { get; init; } = string.Empty;

    [JsonPropertyName("mfrName")]
    public string MfrName { get; init; } = string.Empty;

    [JsonPropertyName("leadTimeDays")]
    public int LeadTimeDays { get; init; }
}
