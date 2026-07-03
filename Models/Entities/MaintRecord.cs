using System.Text.Json.Serialization;

namespace PredictiveAnalysis.Models.Entities;

/// <summary>
/// Entity mapped from mockData/MaintRecord.json. One row per maintenance action,
/// linked to a <see cref="Ticket"/> through <see cref="TicketNo"/>.
/// </summary>
public sealed class MaintRecord
{
    [JsonPropertyName("id")]
    public int Id { get; init; }

    [JsonPropertyName("TicketNo")]
    public int TicketNo { get; init; }

    [JsonPropertyName("partName")]
    public string PartName { get; init; } = string.Empty;

    [JsonPropertyName("downTimeHrs")]
    public double DownTimeHrs { get; init; }

    [JsonPropertyName("noOfPcs")]
    public int NoOfPcs { get; init; }
}
