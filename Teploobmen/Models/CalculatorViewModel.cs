using System.ComponentModel.DataAnnotations;

namespace Teploobmen.Models;

public sealed class CalculatorViewModel
{
    public HeatExchangeInput Input { get; set; } = new();
    public HeatExchangeResult? Result { get; set; }
    [StringLength(100, ErrorMessage = "Название должно содержать не более 100 символов.")]
    public string CalculationName { get; set; } = "Вариант 16";
    public Guid? SavedId { get; set; }
}
