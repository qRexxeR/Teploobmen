using System.Text.Json;
using Teploobmen.Models;

namespace Teploobmen.Services;

/// <summary>Single-process durable storage. An incomplete write never replaces the current file.</summary>
public sealed class CalculationStore
{
    private readonly string path;
    private readonly SemaphoreSlim gate = new(1, 1);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public CalculationStore(IWebHostEnvironment environment)
    {
        var directory = Path.Combine(environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(directory);
        path = Path.Combine(directory, "calculations.json");
    }

    public async Task<IReadOnlyList<SavedCalculation>> ListAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try { return (await ReadAsync(ct)).OrderByDescending(x => x.SavedAt).ToArray(); }
        finally { gate.Release(); }
    }

    public async Task<SavedCalculation?> GetAsync(Guid id, CancellationToken ct = default) =>
        (await ListAsync(ct)).FirstOrDefault(x => x.Id == id);

    public async Task<SavedCalculation> SaveAsync(string name, HeatExchangeInput input, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var items = await ReadAsync(ct);
            var item = new SavedCalculation(Guid.NewGuid(), name.Trim(), DateTimeOffset.UtcNow, input);
            items.Add(item);
            await WriteAsync(items, ct);
            return item;
        }
        finally { gate.Release(); }
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var items = await ReadAsync(ct);
            if (items.RemoveAll(x => x.Id == id) == 0) return false;
            await WriteAsync(items, ct);
            return true;
        }
        finally { gate.Release(); }
    }

    private async Task<List<SavedCalculation>> ReadAsync(CancellationToken ct)
    {
        if (!File.Exists(path)) return [];
        await using var file = File.OpenRead(path);
        // Invalid JSON is reported as an error; existing user data is never silently reset.
        return await JsonSerializer.DeserializeAsync<List<SavedCalculation>>(file, JsonOptions, ct)
            ?? throw new JsonException("Файл сохраненных расчетов пуст или поврежден.");
    }

    private async Task WriteAsync(List<SavedCalculation> items, CancellationToken ct)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await JsonSerializer.SerializeAsync(file, items, JsonOptions, ct);
                await file.FlushAsync(ct);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
