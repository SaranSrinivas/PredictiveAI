using System.Text.Json;
using PredictiveAnalysis.Models;
using RulesEngine.Models;

namespace PredictiveAnalysis.Services;

/// <summary>Outcome of running the RulesEngine over a single machine's features.</summary>
public sealed record RuleEvaluation(double RuleScore, List<string> RiskFactors);

/// <summary>
/// Wraps Microsoft's <see cref="RulesEngine.RulesEngine"/>. The weighted scoring rules are
/// defined in <c>Rules/machine-risk-rules.json</c>; each rule that passes contributes its
/// weight to the score and raises its label as a risk factor.
/// </summary>
public sealed class RiskRulesEngine
{
    private const string WorkflowName = "MachineRiskScoring";

    private readonly RulesEngine.RulesEngine _engine;
    private readonly Dictionary<string, ScoringRule> _rulesByName;

    public RiskRulesEngine(IHostEnvironment env)
    {
        var rulesPath = Path.Combine(env.ContentRootPath, "Rules", "machine-risk-rules.json");
        var definition = JsonSerializer.Deserialize<ScoringWorkflow>(
            File.ReadAllText(rulesPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Unable to load rules from '{rulesPath}'.");

        _rulesByName = definition.Rules.ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);

        var workflow = new Workflow
        {
            WorkflowName = WorkflowName,
            Rules = definition.Rules.Select(r => new Rule
            {
                RuleName = r.Name,
                SuccessEvent = r.Label,
                RuleExpressionType = RuleExpressionType.LambdaExpression,
                Expression = r.Expression
            }).ToList()
        };

        _engine = new RulesEngine.RulesEngine([workflow]);
    }

    public async Task<RuleEvaluation> EvaluateAsync(MachineRiskInput input)
    {
        var parameter = new RuleParameter("machine", input);
        var results = await _engine.ExecuteAllRulesAsync(WorkflowName, parameter);

        double score = 0;
        var factors = new List<string>();

        foreach (var result in results.Where(r => r.IsSuccess))
        {
            if (_rulesByName.TryGetValue(result.Rule.RuleName, out var rule))
            {
                score += rule.Weight;
                factors.Add(rule.Label);
            }
        }

        return new RuleEvaluation(Math.Min(100, Math.Round(score, 2)), factors);
    }

    private sealed class ScoringWorkflow
    {
        public string WorkflowName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public List<ScoringRule> Rules { get; set; } = [];
    }

    private sealed class ScoringRule
    {
        public string Name { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;
        public double Weight { get; set; }
        public string Expression { get; set; } = string.Empty;
    }
}
