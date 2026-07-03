using System.Text.Json.Serialization;

namespace PredictiveAnalysis.Models.Entities;

/// <summary>
/// Entity mapped from mockData/Ticket.json. One row per reported problem for a machine.
/// This is the bridge that links a <see cref="MaintRecord"/> to a <see cref="Machine"/>
/// (MaintRecord.TicketNo == Ticket.TicketNo, Ticket.MachineName == Machine.MachineName).
/// </summary>
public sealed class Ticket
{
    [JsonPropertyName("ticketNo")]
    public int TicketNo { get; init; }

    [JsonPropertyName("Date")]
    public string Date { get; init; } = string.Empty;

    [JsonPropertyName("problem")]
    public string Problem { get; init; } = string.Empty;

    [JsonPropertyName("machineName")]
    public string MachineName { get; init; } = string.Empty;
}
