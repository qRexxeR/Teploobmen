using System.ComponentModel.DataAnnotations;

namespace Teploobmen.Models;

/// <summary>Исходные данные. Теплоемкости задаются в кДж; скорость газа относится к полному сечению шахты.</summary>
public sealed class HeatExchangeInput : IValidatableObject
{
    public const int MaximumPoints = 2001;

    [Display(Name = "Высота слоя, м")]
    [Range(0.000001, 1000000, ErrorMessage = "Высота слоя должна быть от 0,000001 до 1 000 000 м.")]
    public double Height { get; set; } = 6;

    [Display(Name = "Температура материала на входе, °C")]
    [Range(-273.15, 1000000, ErrorMessage = "Температура должна быть не ниже −273,15 °C и не выше 1 000 000 °C.")]
    public double MaterialInletTemperature { get; set; } = 0;

    [Display(Name = "Температура газа на входе, °C")]
    [Range(-273.15, 1000000, ErrorMessage = "Температура должна быть не ниже −273,15 °C и не выше 1 000 000 °C.")]
    public double GasInletTemperature { get; set; } = 700;

    [Display(Name = "Скорость газа, м/с")]
    [Range(0.000000001, 1000000, ErrorMessage = "Скорость газа должна быть положительной и не превышать 1 000 000 м/с.")]
    public double GasVelocity { get; set; } = 0.72;

    [Display(Name = "Теплоемкость газа, кДж/(м³·К)")]
    [Range(0.000000001, 1000000, ErrorMessage = "Объемная теплоемкость газа должна быть положительной и не превышать 1 000 000.")]
    public double GasHeatCapacity { get; set; } = 0.9;

    [Display(Name = "Расход материала, кг/с")]
    [Range(0.000000001, 1000000000, ErrorMessage = "Расход материала должен быть положительным и не превышать 1 000 000 000 кг/с.")]
    public double MaterialFlow { get; set; } = 1.66;

    [Display(Name = "Теплоемкость материала, кДж/(кг·К)")]
    [Range(0.000000001, 1000000, ErrorMessage = "Теплоемкость материала должна быть положительной и не превышать 1 000 000.")]
    public double MaterialHeatCapacity { get; set; } = 1.49;

    [Display(Name = "Объемный коэффициент теплоотдачи, Вт/(м³·К)")]
    [Range(0.000000001, 1000000000000, ErrorMessage = "Коэффициент теплоотдачи должен быть положительным и не превышать 10¹².")]
    public double HeatTransferCoefficient { get; set; } = 2430;

    [Display(Name = "Диаметр аппарата, м")]
    [Range(0.000001, 1000000, ErrorMessage = "Диаметр должен быть от 0,000001 до 1 000 000 м.")]
    public double Diameter { get; set; } = 2.3;

    [Display(Name = "Шаг по высоте, м")]
    [Range(0.000000001, 1000000, ErrorMessage = "Шаг должен быть положительным и не превышать 1 000 000 м.")]
    public double Step { get; set; } = 0.5;

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var property in GetType().GetProperties().Where(p => p.PropertyType == typeof(double)))
        {
            if (!double.IsFinite((double)property.GetValue(this)!))
                yield return new ValidationResult("Введите конечное числовое значение.", [property.Name]);
        }

        if (double.IsFinite(Height) && double.IsFinite(Step) && Height > 0 && Step > 0 &&
            Height / Step > MaximumPoints - 1 + 1e-9)
            yield return new ValidationResult(
                $"Увеличьте шаг: таблица должна содержать не более {MaximumPoints} точек.", [nameof(Step)]);
    }
}
