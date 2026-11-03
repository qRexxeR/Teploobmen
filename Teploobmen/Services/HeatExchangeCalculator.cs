using System.ComponentModel.DataAnnotations;
using Teploobmen.Models;

namespace Teploobmen.Services;

/// <summary>Аналитическое решение стационарного противоточного теплообмена термически тонких кусков.</summary>
public sealed class HeatExchangeCalculator
{
    public HeatExchangeResult Calculate(HeatExchangeInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        Validator.ValidateObject(input, new ValidationContext(input), validateAllProperties: true);

        var area = Math.PI * input.Diameter * input.Diameter / 4;
        var volumeFlow = input.GasVelocity * area;
        var wm = input.MaterialFlow * input.MaterialHeatCapacity * 1000;
        var wg = volumeFlow * input.GasHeatCapacity * 1000;
        var m = wm / wg;
        var y0 = input.HeatTransferCoefficient * input.Height / (input.GasVelocity * input.GasHeatCapacity * 1000);
        var k = (m - 1) / m;
        var temperatureSpan = input.GasInletTemperature - input.MaterialInletTemperature;

        (double Material, double Gas) RelativeTemperatures(double relativeY)
        {
            if (m == 1)
                return (relativeY / (1 + y0), (1 + relativeY) / (1 + y0));

            if (m < 1)
            {
                // Expm1 avoids cancellation when the capacity rates are almost equal.
                var denominator = (1 - m) - m * Expm1(k * y0);
                var material = -Expm1(k * relativeY) / denominator;
                var gas = ((1 - m) - m * Expm1(k * relativeY)) / denominator;
                return (material, gas);
            }

            // Divide numerator and denominator by exp(kY0). Every exponential below
            // has a nonpositive argument, even for extremely tall/high-transfer layers.
            var scaledExponential = Math.Exp(k * (relativeY - y0));
            var scaledDenominator = (m - 1) - Expm1(-k * y0);
            var scaledNumerator = scaledExponential * -Expm1(-k * relativeY);
            var materialRelative = scaledNumerator / scaledDenominator;
            var gasRelative = ((m - 1) * scaledExponential + scaledNumerator) / scaledDenominator;
            return (materialRelative, gasRelative);
        }

        TemperaturePoint Point(double height)
        {
            var relativeY = y0 * (height / input.Height);
            var (material, gas) = RelativeTemperatures(relativeY);
            // Roundoff must not put a calculated temperature outside the two inlet values.
            material = Math.Clamp(material, 0, 1);
            gas = Math.Clamp(gas, 0, 1);
            var t = input.MaterialInletTemperature + temperatureSpan * material;
            var tg = input.MaterialInletTemperature + temperatureSpan * gas;
            if (height == 0) t = input.MaterialInletTemperature;
            if (height == input.Height) tg = input.GasInletTemperature;
            return new TemperaturePoint(height, relativeY, t, tg, tg - t);
        }

        var points = new List<TemperaturePoint>();
        var fullSteps = (int)Math.Floor(input.Height / input.Step);
        for (var i = 0; i <= fullSteps; i++)
        {
            var height = i * input.Step;
            if (height < input.Height - input.Height * 1e-12) points.Add(Point(height));
        }
        points.Add(Point(input.Height));
        if (points.Count > HeatExchangeInput.MaximumPoints)
            throw new ValidationException($"Допускается не более {HeatExchangeInput.MaximumPoints} точек.");

        var materialOutlet = points[^1].MaterialTemperature;
        var gasOutlet = points[0].GasTemperature;
        var qm = wm * (materialOutlet - input.MaterialInletTemperature);
        var qg = wg * (input.GasInletTemperature - gasOutlet);
        var effectiveness = temperatureSpan == 0 ? 0 : qm / (Math.Min(wm, wg) * temperatureSpan);

        return new HeatExchangeResult
        {
            Input = input, Area = area, GasVolumeFlow = volumeFlow,
            MaterialCapacityRate = wm, GasCapacityRate = wg, CapacityRatio = m,
            TotalRelativeHeight = y0, MaterialOutletTemperature = materialOutlet,
            GasOutletTemperature = gasOutlet, HeatFlowKw = qm / 1000,
            Effectiveness = Math.Clamp(effectiveness, 0, 1), EnergyBalanceResidual = qm - qg,
            Points = points.AsReadOnly()
        };
    }

    private static double Expm1(double x)
    {
        if (Math.Abs(x) >= 0.00001) return Math.Exp(x) - 1;
        // Six terms suffice well below double precision at |x| < 10^-5.
        return x * (1 + x * (0.5 + x * (1.0 / 6 + x * (1.0 / 24 + x * (1.0 / 120 + x / 720)))));
    }
}
