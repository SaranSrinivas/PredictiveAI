namespace PredictiveAnalysis.Models.Entities;

/// <summary>
/// Projection produced by joining <see cref="MaintRecord"/> to <see cref="Ticket"/> on
/// <c>TicketNo</c>. This is the entity that finally attaches maintenance/downtime data to a
/// specific machine (via <see cref="MachineName"/>), which is what the ML model trains on.
/// </summary>
public sealed class MaintenanceTicket
{
    public int TicketNo { get; init; }
    public string MachineName { get; init; } = string.Empty;
    public string Problem { get; init; } = string.Empty;
    public string Date { get; init; } = string.Empty;
    public string PartName { get; init; } = string.Empty;
    public double DownTimeHrs { get; init; }
    public int NoOfPcs { get; init; }
}
