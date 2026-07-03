using System.Text.Json;
using PredictiveAnalysis.Models;
using PredictiveAnalysis.Models.Entities;

namespace PredictiveAnalysis.Services;

/// <summary>
/// Per-machine feature row produced from the joined mock data. This is the unit the
/// RulesEngine and the ML.NET model both consume.
/// </summary>
public sealed class MachineFeatureSet
{
    public required Machine Machine { get; init; }
    public int DaysSinceLastMaintenance { get; init; }
    public int TicketCount { get; init; }
    public int MaintEventCount { get; init; }
    public double AvgDowntimeHours { get; init; }
    public int HighDowntimePartsCount { get; init; }
    public string TopProblem { get; init; } = string.Empty;
    public IReadOnlyList<Part> RelatedParts { get; init; } = [];
}

/// <summary>
/// Loads the four JSON entities and derives per-machine feature sets. The key operation is
/// joining <see cref="MaintRecord"/> to <see cref="Ticket"/> on <c>TicketNo</c> so that
/// downtime/parts data can be attributed to the machine named on the ticket.
/// </summary>
public sealed class DataRepository
{
    private const double HighDowntimeThresholdHours = 50.0;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _machinePath;
    private readonly string _maintenancePath;
    private readonly string _partsPath;
    private readonly string _ticketPath;

    public DataRepository(IHostEnvironment env)
    {
        var dataDir = Path.Combine(env.ContentRootPath, "mockData");
        _machinePath = Path.Combine(dataDir, "Machine.json");
        _maintenancePath = Path.Combine(dataDir, "MaintRecord.json");
        _partsPath = Path.Combine(dataDir, "Parts.json");
        _ticketPath = Path.Combine(dataDir, "Ticket.json");
    }

    public List<Machine> LoadMachines() => Load<Machine>(_machinePath);

    public List<MaintRecord> LoadMaintRecords() => Load<MaintRecord>(_maintenancePath);

    public List<Part> LoadParts() => Load<Part>(_partsPath);

    public List<Ticket> LoadTickets() => Load<Ticket>(_ticketPath);

    /// <summary>
    /// Inner-joins maintenance records to tickets on TicketNo, attaching each maintenance
    /// action to the machine named on its ticket.
    /// </summary>
    public List<MaintenanceTicket> JoinMaintenanceWithTickets(
        IEnumerable<MaintRecord> maintRecords,
        IEnumerable<Ticket> tickets)
    {
        var ticketsByNo = tickets
            .GroupBy(t => t.TicketNo)
            .ToDictionary(g => g.Key, g => g.First());

        var joined = new List<MaintenanceTicket>();
        foreach (var record in maintRecords)
        {
            if (!ticketsByNo.TryGetValue(record.TicketNo, out var ticket))
            {
                continue; // No matching ticket -> cannot attribute to a machine.
            }

            joined.Add(new MaintenanceTicket
            {
                TicketNo = record.TicketNo,
                MachineName = ticket.MachineName,
                Problem = ticket.Problem,
                Date = ticket.Date,
                PartName = record.PartName,
                DownTimeHrs = record.DownTimeHrs,
                NoOfPcs = record.NoOfPcs
            });
        }

        return joined;
    }

    /// <summary>
    /// Produces one <see cref="MachineFeatureSet"/> per distinct machine, aggregating the
    /// joined maintenance/ticket data.
    /// </summary>
    public List<MachineFeatureSet> BuildFeatureSets()
    {
        var machines = LoadMachines();
        var maintRecords = LoadMaintRecords();
        var parts = LoadParts();
        var tickets = LoadTickets();

        var maintenanceTickets = JoinMaintenanceWithTickets(maintRecords, tickets);

        var maintByMachine = maintenanceTickets
            .GroupBy(mt => mt.MachineName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var ticketsByMachine = tickets
            .GroupBy(t => t.MachineName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var featureSets = new List<MachineFeatureSet>();

        // Distinct machines (the seed data repeats a couple of machine names).
        foreach (var machine in machines.GroupBy(m => m.MachineName, StringComparer.OrdinalIgnoreCase).Select(g => g.First()))
        {
            maintByMachine.TryGetValue(machine.MachineName, out var machineMaint);
            machineMaint ??= [];
            ticketsByMachine.TryGetValue(machine.MachineName, out var machineTickets);
            machineTickets ??= [];

            var avgDowntime = machineMaint.Count > 0 ? machineMaint.Average(m => m.DownTimeHrs) : 0.0;
            var highDowntimeParts = machineMaint.Count(m => m.DownTimeHrs > HighDowntimeThresholdHours);

            var topProblem = machineTickets
                .GroupBy(t => t.Problem, StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .FirstOrDefault() ?? string.Empty;

            // Parts referenced in this machine's maintenance history, enriched from the parts catalog.
            var partNames = machineMaint.Select(m => m.PartName).Distinct(StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var relatedParts = parts.Where(p => partNames.Contains(p.PartName)).ToList();

            featureSets.Add(new MachineFeatureSet
            {
                Machine = machine,
                DaysSinceLastMaintenance = DateHelper.CalculateDaysSince(machine.LastMaintDate),
                TicketCount = machineTickets.Count,
                MaintEventCount = machineMaint.Count,
                AvgDowntimeHours = Math.Round(avgDowntime, 2),
                HighDowntimePartsCount = highDowntimeParts,
                TopProblem = topProblem,
                RelatedParts = relatedParts
            });
        }

        return featureSets;
    }

    private static List<T> Load<T>(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<List<T>>(json, JsonOptions) ?? [];
    }
}
