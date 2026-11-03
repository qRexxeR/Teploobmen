using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Teploobmen.Models;
using Teploobmen.Services;

namespace Teploobmen.Controllers;

public sealed class CalculatorController(HeatExchangeCalculator calculator, CalculationStore store,
    ILogger<CalculatorController> logger) : Controller
{
    [HttpGet]
    public IActionResult Index() => View(new CalculatorViewModel { Result = calculator.Calculate(new()) });

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Calculate(CalculatorViewModel model)
    {
        TryCalculate(model);
        return View("Index", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(CalculatorViewModel model, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(model.CalculationName))
            ModelState.AddModelError(nameof(model.CalculationName), "Укажите название варианта.");
        if (!TryCalculate(model)) return View("Index", model);
        try
        {
            var item = await store.SaveAsync(model.CalculationName, model.Input, ct);
            TempData["Success"] = "Вариант сохранён. Его можно открыть в разделе «Сохранённые расчёты».";
            return RedirectToAction(nameof(Load), new { id = item.Id });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogError(ex, "Could not save calculation");
            ModelState.AddModelError("", "Не удалось сохранить вариант. Проверьте доступ к папке App_Data и файл расчетов.");
            return View("Index", model);
        }
    }

    [HttpGet]
    public async Task<IActionResult> Saved(CancellationToken ct)
    {
        try { return View(await store.ListAsync(ct)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogError(ex, "Could not read calculations");
            TempData["Error"] = "Не удалось прочитать сохраненные расчеты. Данные на диске не изменены.";
            return View(Array.Empty<SavedCalculation>());
        }
    }

    [HttpGet]
    public async Task<IActionResult> Load(Guid id, CancellationToken ct)
    {
        var item = await store.GetAsync(id, ct);
        if (item is null) return NotFound("Сохраненный расчет не найден.");
        var model = new CalculatorViewModel { Input = item.Input, CalculationName = item.Name, SavedId = item.Id };
        TryCalculate(model);
        return View("Index", model);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        try
        {
            TempData["Success"] = await store.DeleteAsync(id, ct) ? "Вариант удалён." : "Вариант уже отсутствует.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogError(ex, "Could not delete calculation");
            TempData["Error"] = "Не удалось удалить вариант. Файл расчетов не изменён.";
        }
        return RedirectToAction(nameof(Saved));
    }

    [HttpGet]
    public IActionResult Method() => View();

    [HttpGet]
    public IActionResult Error() => View();

    [HttpGet]
    public async Task<IActionResult> Csv(Guid? id, CancellationToken ct)
    {
        HeatExchangeInput input = new();
        if (id.HasValue)
        {
            var item = await store.GetAsync(id.Value, ct);
            if (item is null) return NotFound();
            input = item.Input;
        }
        return CsvFile(calculator.Calculate(input));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Export(CalculatorViewModel model)
    {
        if (!TryCalculate(model)) return View("Index", model);
        return CsvFile(model.Result!);
    }

    private FileContentResult CsvFile(HeatExchangeResult result)
    {
        var csv = new StringBuilder("y, м;Y;t материала, °C;T газа, °C;T − t, °C\r\n");
        foreach (var p in result.Points)
            csv.AppendLine(string.Join(';', new[] { p.Height, p.RelativeHeight, p.MaterialTemperature,
                p.GasTemperature, p.DeltaTemperature }.Select(x => x.ToString("G12", CultureInfo.GetCultureInfo("ru-RU")))));
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray(),
            "text/csv; charset=utf-8", "heat-exchange.csv");
    }

    private bool TryCalculate(CalculatorViewModel model)
    {
        if (!ModelState.IsValid) return false;
        try { model.Result = calculator.Calculate(model.Input); return true; }
        catch (ValidationException ex) { ModelState.AddModelError("", ex.Message); }
        catch (ArgumentException ex) { ModelState.AddModelError("", ex.Message); }
        return false;
    }
}
