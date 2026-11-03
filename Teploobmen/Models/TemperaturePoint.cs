namespace Teploobmen.Models;

/// <summary>Координата y направлена сверху вниз; RelativeHeight — безразмерная координата Y.</summary>
public sealed record TemperaturePoint(
    double Height,
    double RelativeHeight,
    double MaterialTemperature,
    double GasTemperature,
    double DeltaTemperature);
