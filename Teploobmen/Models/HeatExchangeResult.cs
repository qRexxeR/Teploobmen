namespace Teploobmen.Models;

public sealed class HeatExchangeResult
{
    public required HeatExchangeInput Input { get; init; }
    public required double Area { get; init; }
    public required double GasVolumeFlow { get; init; }
    public required double MaterialCapacityRate { get; init; }
    public required double GasCapacityRate { get; init; }
    public required double CapacityRatio { get; init; }
    public required double TotalRelativeHeight { get; init; }
    public required double MaterialOutletTemperature { get; init; }
    public required double GasOutletTemperature { get; init; }
    /// <summary>Знаковая мощность в кВт: положительна при нагреве материала, отрицательна при охлаждении.</summary>
    public required double HeatFlowKw { get; init; }
    /// <summary>Доля максимально возможного теплового потока; при равных входных температурах равна нулю.</summary>
    public required double Effectiveness { get; init; }
    /// <summary>Qматериала − Qгаза, Вт, без промежуточного округления.</summary>
    public required double EnergyBalanceResidual { get; init; }
    public required IReadOnlyList<TemperaturePoint> Points { get; init; }
}
