using PredictiveAnalysis.Models;

namespace PredictiveAnalysis.Services;

/// <summary>
/// Orchestrates the predictive-maintenance dashboard: it loads the joined entity data, scores
/// each machine with the RulesEngine, blends in the ML.NET probability, and enriches the result
/// with Semantic Kernel narratives.
/// </summary>
public sealed class PredictiveAnalysisService
{
    // Blend weights for the final overall score.
    private const double RuleWeight = 0.55;
    private const double MlWeight = 0.45;

    private readonly DataRepository _repository;
    private readonly RiskRulesEngine _rulesEngine;
    private readonly RiskModelTrainer _modelTrainer;
    private readonly NarrativeService _narrativeService;

    private readonly int _maxNarratives;

    public PredictiveAnalysisService(
        DataRepository repository,
        RiskRulesEngine rulesEngine,
        RiskModelTrainer modelTrainer,
        NarrativeService narrativeService,
        IConfiguration configuration)
    {
        _repository = repository;
        _rulesEngine = rulesEngine;
        _modelTrainer = modelTrainer;
        _narrativeService = narrativeService;
        _maxNarratives = configuration.GetSection("SemanticKernel").GetValue("MaxNarratives", 8);
    }

    /// <summary>
    /// Fast path: joins the data, scores with RulesEngine + ML.NET, and fills in template
    /// recommendations. Does NOT call the LLM, so it returns immediately and the page can render.
    /// Call <see cref="EnrichNarrativesAsync"/> afterwards to stream in Semantic Kernel narratives.
    /// </summary>
    public async Task<DashboardSummary> AnalyzeAsync(CancellationToken cancellationToken = default)
    {
        var featureSets = _repository.BuildFeatureSets();

        // Train the ML.NET model once on the joined per-machine data.
        _modelTrainer.EnsureTrained(() => featureSets);

        var items = new List<MachineRiskItem>();

        foreach (var features in featureSets)
        {
            var input = new MachineRiskInput
            {
                DaysSinceLastMaintenance = features.DaysSinceLastMaintenance,
                MaintenanceIntervalDays = features.Machine.MaintSchInDays,
                TicketCount = features.TicketCount,
                AvgDowntimeHours = (float)features.AvgDowntimeHours,
                HighDowntimePartsCount = features.HighDowntimePartsCount
            };

            var ruleEvaluation = await _rulesEngine.EvaluateAsync(input);
            var prediction = _modelTrainer.Predict(input);

            var overallRisk = Math.Round(
                Math.Min(100, ruleEvaluation.RuleScore * RuleWeight + prediction.Probability * 100 * MlWeight), 2);

            items.Add(new MachineRiskItem
            {
                Code = features.Machine.Code,
                MachineName = features.Machine.MachineName,
                CostCenter = features.Machine.CostCenter,
                LastMaintDate = features.Machine.LastMaintDate,
                MaintenanceIntervalDays = features.Machine.MaintSchInDays,
                DaysSinceLastMaintenance = features.DaysSinceLastMaintenance,
                TicketCount = features.TicketCount,
                AvgDowntimeHours = features.AvgDowntimeHours,
                HighDowntimePartsCount = features.HighDowntimePartsCount,
                RuleScore = ruleEvaluation.RuleScore,
                MlRiskProbability = Math.Round(prediction.Probability, 4),
                OverallRiskScore = overallRisk,
                Severity = DetermineSeverity(overallRisk),
                TopProblem = features.TopProblem,
                RiskFactors = ruleEvaluation.RiskFactors
            });
        }

        var ordered = items.OrderByDescending(i => i.OverallRiskScore).ToList();

        // Template narratives so the page is fully usable before the LLM runs.
        foreach (var item in ordered)
        {
            item.Recommendation = NarrativeService.BuildTemplateRecommendation(item);
        }

        var summary = new DashboardSummary
        {
            TotalMachines = ordered.Count,
            CriticalMachines = ordered.Count(i => i.Severity == "Critical"),
            HighRiskMachines = ordered.Count(i => i.Severity is "High" or "Critical"),
            AvgRiskScore = ordered.Count > 0 ? Math.Round(ordered.Average(i => i.OverallRiskScore), 2) : 0,
            Machines = ordered
        };
        summary.FleetSummary = NarrativeService.BuildTemplateFleetSummary(summary);

        return summary;
    }

    /// <summary>
    /// Whether Semantic Kernel/Ollama narratives are available. If false, the template text from
    /// <see cref="AnalyzeAsync"/> is already the final result and enrichment can be skipped.
    /// </summary>
    public bool LlmNarrativesEnabled => _narrativeService.IsLlmEnabled;

    /// <summary>
    /// Streams Semantic Kernel narratives into an already-computed summary: an LLM recommendation
    /// for the highest-risk machines (bounded by <c>MaxNarratives</c>) plus one LLM fleet summary.
    /// <paramref name="onProgress"/> is invoked after each update so the UI can refresh live.
    /// </summary>
    public async Task EnrichNarrativesAsync(DashboardSummary summary, Func<Task>? onProgress = null, CancellationToken cancellationToken = default)
    {
        var top = summary.Machines.Take(_maxNarratives).ToList();
        foreach (var item in top)
        {
            cancellationToken.ThrowIfCancellationRequested();
            item.Recommendation = await _narrativeService.GenerateRecommendationAsync(item, cancellationToken);
            if (onProgress is not null)
            {
                await onProgress();
            }
        }

        summary.FleetSummary = await _narrativeService.GenerateFleetSummaryAsync(summary, cancellationToken);
        if (onProgress is not null)
        {
            await onProgress();
        }
    }

    private static string DetermineSeverity(double score) =>
        score >= 80 ? "Critical" : score >= 60 ? "High" : score >= 35 ? "Medium" : "Low";
}
