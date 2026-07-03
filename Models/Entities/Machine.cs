using System.Text.Json.Serialization;

namespace PredictiveAnalysis.Models.Entities;

/// <summary>
/// Entity mapped from mockData/Machine.json. One row per physical asset.
/// </summary>
public sealed class Machine
{
    [JsonPropertyName("code")]
    public int Code { get; init; }

    [JsonPropertyName("costCenter")]
    public string CostCenter { get; init; } = string.Empty;

    [JsonPropertyName("machineName")]
    public string MachineName { get; init; } = string.Empty;

    [JsonPropertyName("mfrName")]
    public string MfrName { get; init; } = string.Empty;

    [JsonPropertyName("commDate")]
    public string CommDate { get; init; } = string.Empty;

    [JsonPropertyName("maintSchInDays")]
    public int MaintSchInDays { get; init; }

    [JsonPropertyName("lastMaintDate")]
    public string LastMaintDate { get; init; } = string.Empty;
}
