using Microsoft.SemanticKernel;
using PredictiveAnalysis.Models;

namespace PredictiveAnalysis.Services;

/// <summary>
/// Generates natural-language maintenance narratives with Microsoft Semantic Kernel backed by
/// a local Ollama model. If Semantic Kernel is disabled or Ollama is unreachable, every method
/// degrades gracefully to a deterministic template so the dashboard always renders.
/// </summary>
public sealed class NarrativeService
{
    private readonly ILogger<NarrativeService> _logger;
    private readonly Kernel? _kernel;
    private readonly bool _enabled;

    public NarrativeService(IConfiguration configuration, ILogger<NarrativeService> logger)
    {
        _logger = logger;

        var section = configuration.GetSection("SemanticKernel");
        _enabled = section.GetValue("Enabled", true);
        var endpoint = section.GetValue<string>("Ollama:Endpoint") ?? "http://localhost:11434";
        var modelId = section.GetValue<string>("Ollama:ModelId") ?? "llama3.2";

        if (!_enabled)
        {
            return;
        }

        try
        {
            var builder = Kernel.CreateBuilder();
            builder.AddOllamaChatCompletion(modelId, new Uri(endpoint));
            _kernel = builder.Build();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Semantic Kernel/Ollama initialization failed; using template narratives.");
            _kernel = null;
        }
    }

    public bool IsLlmEnabled => _enabled && _kernel is not null;

    /// <summary>Generates a concise, actionable recommendation for a single machine.</summary>
    public async Task<string> GenerateRecommendationAsync(MachineRiskItem item, CancellationToken cancellationToken = default)
    {
        if (_kernel is null)
        {
            return BuildTemplateRecommendation(item);
        }

        var factors = item.RiskFactors.Count > 0 ? string.Join("; ", item.RiskFactors) : "none flagged";
        var prompt =
            $"""
             You are a reliability engineer. Write ONE short, actionable maintenance recommendation
             (max 2 sentences, no preamble, no markdown) for this asset.

             Machine: {item.MachineName} (cost center {item.CostCenter})
             Severity: {item.Severity} (overall risk {item.OverallRiskScore}/100)
             Days since last maintenance: {item.DaysSinceLastMaintenance} (scheduled every {item.MaintenanceIntervalDays} days)
             Breakdown tickets: {item.TicketCount}, avg downtime {item.AvgDowntimeHours}h
             Most common problem: {(string.IsNullOrWhiteSpace(item.TopProblem) ? "n/a" : item.TopProblem)}
             Risk factors: {factors}
             """;

        try
        {
            var result = await _kernel.InvokePromptAsync(prompt, cancellationToken: cancellationToken);
            var text = result.GetValue<string>()?.Trim();
            return string.IsNullOrWhiteSpace(text) ? BuildTemplateRecommendation(item) : text;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM recommendation failed for {Machine}; using template.", item.MachineName);
            return BuildTemplateRecommendation(item);
        }
    }

    /// <summary>Generates a one-paragraph fleet-health summary for the whole dashboard.</summary>
    public async Task<string> GenerateFleetSummaryAsync(DashboardSummary summary, CancellationToken cancellationToken = default)
    {
        if (_kernel is null)
        {
            return BuildTemplateFleetSummary(summary);
        }

        var topMachines = summary.Machines
            .Take(5)
            .Select(m => $"{m.MachineName} ({m.Severity}, {m.OverallRiskScore}/100)");
        var prompt =
            $"""
             You are a maintenance operations lead. In 2-3 sentences (plain text, no markdown),
             summarize the fleet's maintenance health and the single most important next action.

             Total machines: {summary.TotalMachines}
             Critical: {summary.CriticalMachines}, High risk (incl. critical): {summary.HighRiskMachines}
             Average risk score: {summary.AvgRiskScore}/100
             Highest-risk machines: {string.Join(", ", topMachines)}
             """;

        try
        {
            var result = await _kernel.InvokePromptAsync(prompt, cancellationToken: cancellationToken);
            var text = result.GetValue<string>()?.Trim();
            return string.IsNullOrWhiteSpace(text) ? BuildTemplateFleetSummary(summary) : text;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "LLM fleet summary failed; using template.");
            return BuildTemplateFleetSummary(summary);
        }
    }

    public static string BuildTemplateRecommendation(MachineRiskItem item)
    {
        return item.Severity switch
        {
            "Critical" => $"Immediate inspection required: {item.MachineName} is {item.DaysSinceLastMaintenance} days into a {item.MaintenanceIntervalDays}-day cycle with {item.TicketCount} tickets. Plan parts replacement now.",
            "High" => $"Schedule preventive maintenance for {item.MachineName} within 7 days and review high-impact spares ({item.RiskFactors.Count} risk factors).",
            "Medium" => $"Monitor {item.MachineName} closely; service is recommended soon given {item.TicketCount} tickets and {item.AvgDowntimeHours}h average downtime.",
            _ => item.TicketCount > 0
                ? $"No urgent action for {item.MachineName}; continue routine checks and monitor tickets."
                : $"No action needed for {item.MachineName}; maintain the current maintenance cadence."
        };
    }

    public static string BuildTemplateFleetSummary(DashboardSummary summary)
    {
        var lead = summary.Machines.FirstOrDefault();
        var focus = lead is null ? "the fleet" : lead.MachineName;
        return $"{summary.TotalMachines} machines assessed with an average risk of {summary.AvgRiskScore}/100; " +
               $"{summary.CriticalMachines} critical and {summary.HighRiskMachines} high-risk. " +
               $"Prioritize {focus} for the next maintenance window.";
    }
}
