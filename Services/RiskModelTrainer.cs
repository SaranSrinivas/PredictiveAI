using Microsoft.ML;
using Microsoft.ML.Data;
using PredictiveAnalysis.Models;

namespace PredictiveAnalysis.Services;

/// <summary>
/// Trains the ML.NET binary classifier on the real, joined per-machine feature sets
/// (MaintRecord ⋈ Ticket on TicketNo, aggregated per machine) rather than on hardcoded rows.
///
/// The mock data has no ground-truth failure label, so a label is derived heuristically from
/// the observed history: a machine is treated as "high risk" when it is overdue AND shows a
/// strong reliability signal (high downtime, many tickets, or repeated high-downtime parts).
/// The classifier then learns to generalize that pattern into a probability.
/// </summary>
public sealed class RiskModelTrainer
{
    private readonly MLContext _mlContext = new(seed: 42);
    private readonly object _predictLock = new();
    private readonly object _trainLock = new();

    private ITransformer? _model;
    private PredictionEngine<MachineRiskTrainingRow, MachineRiskPredictionRow>? _predictionEngine;
    private int _trainingRowCount;
    private int _positiveLabelCount;
    private bool _trained;

    public int TrainingRowCount => _trainingRowCount;
    public int PositiveLabelCount => _positiveLabelCount;

    /// <summary>Trains the model once (thread-safe); subsequent calls are no-ops.</summary>
    public void EnsureTrained(Func<IReadOnlyList<MachineFeatureSet>> featureSetFactory)
    {
        if (_trained)
        {
            return;
        }

        lock (_trainLock)
        {
            if (_trained)
            {
                return;
            }

            Train(featureSetFactory());
            _trained = true;
        }
    }

    /// <summary>Trains the model from the supplied feature sets. Safe to call once at startup.</summary>
    public void Train(IReadOnlyList<MachineFeatureSet> featureSets)
    {
        var rows = featureSets.Select(ToTrainingRow).ToList();
        _trainingRowCount = rows.Count;
        _positiveLabelCount = rows.Count(r => r.Label);

        // SDCA needs both classes present; if the heuristic produced only one, skip training
        // and fall back to a neutral probability at predict time.
        if (_positiveLabelCount == 0 || _positiveLabelCount == rows.Count)
        {
            _model = null;
            _predictionEngine = null;
            return;
        }

        var dataView = _mlContext.Data.LoadFromEnumerable(rows);

        var pipeline = _mlContext.Transforms.Concatenate("Features",
                nameof(MachineRiskTrainingRow.DaysSinceLastMaintenance),
                nameof(MachineRiskTrainingRow.MaintenanceIntervalDays),
                nameof(MachineRiskTrainingRow.TicketCount),
                nameof(MachineRiskTrainingRow.AvgDowntimeHours),
                nameof(MachineRiskTrainingRow.HighDowntimePartsCount))
            .Append(_mlContext.Transforms.NormalizeMinMax("Features"))
            .Append(_mlContext.BinaryClassification.Trainers.SdcaLogisticRegression(
                labelColumnName: nameof(MachineRiskTrainingRow.Label),
                featureColumnName: "Features"));

        _model = pipeline.Fit(dataView);
        _predictionEngine = _mlContext.Model.CreatePredictionEngine<MachineRiskTrainingRow, MachineRiskPredictionRow>(_model);
    }

    public MachineRiskPrediction Predict(MachineRiskInput input)
    {
        if (_predictionEngine is null)
        {
            return new MachineRiskPrediction { PredictedLabel = false, Probability = 0.2f, Score = 0f };
        }

        MachineRiskPredictionRow result;
        // PredictionEngine is not thread-safe.
        lock (_predictLock)
        {
            result = _predictionEngine.Predict(new MachineRiskTrainingRow
            {
                DaysSinceLastMaintenance = input.DaysSinceLastMaintenance,
                MaintenanceIntervalDays = input.MaintenanceIntervalDays,
                TicketCount = input.TicketCount,
                AvgDowntimeHours = input.AvgDowntimeHours,
                HighDowntimePartsCount = input.HighDowntimePartsCount
            });
        }

        return new MachineRiskPrediction
        {
            PredictedLabel = result.PredictedLabel,
            Probability = (float)Math.Round(result.Probability, 4),
            Score = (float)Math.Round(result.Score, 4)
        };
    }

    private static MachineRiskTrainingRow ToTrainingRow(MachineFeatureSet f)
    {
        var overdue = f.DaysSinceLastMaintenance > f.Machine.MaintSchInDays;
        var reliabilitySignal = f.AvgDowntimeHours >= 50 || f.TicketCount >= 4 || f.HighDowntimePartsCount >= 2;

        return new MachineRiskTrainingRow
        {
            DaysSinceLastMaintenance = f.DaysSinceLastMaintenance,
            MaintenanceIntervalDays = f.Machine.MaintSchInDays,
            TicketCount = f.TicketCount,
            AvgDowntimeHours = (float)f.AvgDowntimeHours,
            HighDowntimePartsCount = f.HighDowntimePartsCount,
            Label = overdue && reliabilitySignal
        };
    }
}

public sealed class MachineRiskTrainingRow
{
    [LoadColumn(0)]
    public float DaysSinceLastMaintenance { get; set; }

    [LoadColumn(1)]
    public float MaintenanceIntervalDays { get; set; }

    [LoadColumn(2)]
    public float TicketCount { get; set; }

    [LoadColumn(3)]
    public float AvgDowntimeHours { get; set; }

    [LoadColumn(4)]
    public float HighDowntimePartsCount { get; set; }

    [LoadColumn(5)]
    public bool Label { get; set; }
}

public sealed class MachineRiskPredictionRow
{
    [ColumnName("PredictedLabel")]
    public bool PredictedLabel { get; set; }

    [ColumnName("Probability")]
    public float Probability { get; set; }

    [ColumnName("Score")]
    public float Score { get; set; }
}
