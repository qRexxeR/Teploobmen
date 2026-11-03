using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Teploobmen.Models;
using Teploobmen.Services;

var calculator = new HeatExchangeCalculator();
var tests = new (string Name, Action Run)[]
{
    ("Variant 16 reference values", () =>
    {
        var r = calculator.Calculate(new());
        Near(r.MaterialOutletTemperature, 691.1148593801612, 1e-9);
        Near(r.GasOutletTemperature, 65.07266055244398, 1e-9);
        Near(r.HeatFlowKw, 1709.4034931908904, 1e-9);
        Near(r.EnergyBalanceResidual, 0, 1e-6);
        Require(r.Points.Count == 13, "Expected 13 table points");
        Boundaries(r);
    }),
    ("Cooling reverses signed heat flow", () =>
    {
        var r = calculator.Calculate(new() { MaterialInletTemperature = 700, GasInletTemperature = 0 });
        Near(r.MaterialOutletTemperature, 700 - 691.1148593801612, 1e-9);
        Near(r.GasOutletTemperature, 700 - 65.07266055244398, 1e-9);
        Require(r.HeatFlowKw < 0, "Cooling must have negative heat flow");
        Require(r.Points.All(p => p.DeltaTemperature <= 0), "Gas must stay colder");
        Boundaries(r);
    }),
    ("Equal capacity rates and both neighboring limits", () =>
    {
        // Both capacity rates use exactly the same floating-point operations: pi * 1000.
        var input = new HeatExchangeInput { Diameter = 2, GasVelocity = 1, GasHeatCapacity = 1,
            MaterialFlow = Math.PI, MaterialHeatCapacity = 1, HeatTransferCoefficient = 1000, Height = 2 };
        var r = calculator.Calculate(input);
        Near(r.CapacityRatio, 1, 1e-15);
        var expected = 700 * r.TotalRelativeHeight / (1 + r.TotalRelativeHeight);
        Near(r.MaterialOutletTemperature, expected, 1e-9);
        foreach (var factor in new[] { 1 - 1e-12, 1 + 1e-12 })
        {
            input.MaterialFlow = Math.PI * factor;
            var near = calculator.Calculate(input);
            Near(near.MaterialOutletTemperature, expected, 1e-6);
            Near(near.EnergyBalanceResidual, 0, 1e-6);
            Boundaries(near);
        }
    }),
    ("Equal inlet temperatures produce no heat flow", () =>
    {
        var r = calculator.Calculate(new() { MaterialInletTemperature = 100, GasInletTemperature = 100 });
        Require(r.Points.All(p => p.MaterialTemperature == 100 && p.GasTemperature == 100), "Uniform field required");
        Near(r.HeatFlowKw, 0, 0); Near(r.Effectiveness, 0, 0);
    }),
    ("Non-dividing and oversized mesh steps include exact endpoint", () =>
    {
        foreach (var step in new[] { 0.7, 10.0, 0.003 })
        {
            var r = calculator.Calculate(new() { Step = step });
            Near(r.Points[^1].Height, 6, 0);
            Require(r.Points.Select(p => p.Height).Distinct().Count() == r.Points.Count, "Duplicate mesh heights");
            Require(r.Points.Count <= 2001, "Point limit exceeded");
            Boundaries(r);
        }
    }),
    ("Invalid, nonfinite and oversized meshes rejected", () =>
    {
        foreach (var property in typeof(HeatExchangeInput).GetProperties().Where(p => p.PropertyType == typeof(double)))
        foreach (var value in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var input = new HeatExchangeInput(); property.SetValue(input, value);
            Reject(input);
        }
        foreach (var input in new HeatExchangeInput[]
        {
            new() { Height = 0 }, new() { Diameter = -1 }, new() { Step = 0 },
            new() { GasVelocity = 0 }, new() { GasHeatCapacity = 0 }, new() { MaterialFlow = 0 },
            new() { MaterialHeatCapacity = 0 }, new() { HeatTransferCoefficient = 0 },
            new() { Step = 0.00001 }, new() { GasInletTemperature = -300 }
        }) Reject(input);
    }),
    ("Large transfer exponent remains finite for m greater than one", () =>
    {
        var r = calculator.Calculate(new() { MaterialFlow = 3, HeatTransferCoefficient = 1e12 });
        Require(r.CapacityRatio > 1, "Expected m > 1");
        Require(r.Points.All(p => double.IsFinite(p.MaterialTemperature) && double.IsFinite(p.GasTemperature)), "Nonfinite temperature");
        Require(r.MaterialOutletTemperature < 700, "Capacity-limited outlet must stay below 700");
        Near(r.EnergyBalanceResidual, 0, 1e-5);
        Boundaries(r);
    }),
    ("Very weak heat transfer remains stable near m equals one", () =>
    {
        var input = new HeatExchangeInput { Diameter = 2, GasVelocity = 1 / Math.PI, GasHeatCapacity = 1,
            MaterialFlow = 1 + 1e-12, MaterialHeatCapacity = 1, HeatTransferCoefficient = 1e-9 };
        var r = calculator.Calculate(input);
        Require(r.Points.All(p => double.IsFinite(p.DeltaTemperature)), "Nonfinite weak-transfer field");
        Require(r.MaterialOutletTemperature >= 0 && r.MaterialOutletTemperature < 1e-6, "Weak-transfer material outlet");
        Boundaries(r);
    }),
    ("Saved variants survive restart, parallel writes and deletion", () => StoragePersistence().GetAwaiter().GetResult()),
    ("Corrupt JSON is reported without replacing existing data", () => CorruptStorage().GetAwaiter().GetResult())
};

var failed = 0;
foreach (var test in tests)
{
    try { test.Run(); Console.WriteLine($"PASS {test.Name}"); }
    catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL {test.Name}: {ex.Message}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} tests passed.");
return failed == 0 ? 0 : 1;

void Reject(HeatExchangeInput input)
{
    try { calculator.Calculate(input); }
    catch (ValidationException) { return; }
    throw new Exception("Invalid input was accepted");
}
static void Boundaries(HeatExchangeResult r)
{
    Near(r.Points[0].MaterialTemperature, r.Input.MaterialInletTemperature, 0);
    Near(r.Points[^1].GasTemperature, r.Input.GasInletTemperature, 0);
    var min = Math.Min(r.Input.MaterialInletTemperature, r.Input.GasInletTemperature);
    var max = Math.Max(r.Input.MaterialInletTemperature, r.Input.GasInletTemperature);
    Require(r.Points.All(p => p.MaterialTemperature >= min - 1e-9 && p.MaterialTemperature <= max + 1e-9 &&
        p.GasTemperature >= min - 1e-9 && p.GasTemperature <= max + 1e-9), "Temperature outside inlet bounds");
}
static void Near(double actual, double expected, double tolerance)
{
    if (!double.IsFinite(actual) || Math.Abs(actual - expected) > tolerance)
        throw new Exception($"Expected {expected:G17}, actual {actual:G17}, tolerance {tolerance}");
}
static void Require(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

static async Task StoragePersistence()
{
    var directory = TestDirectory();
    try
    {
        var environment = new TestEnvironment(directory);
        var store = new CalculationStore(environment);
        var saved = await store.SaveAsync("  Variant 16  ", new HeatExchangeInput());
        Require(saved.Name == "Variant 16", "Saved name must be trimmed");
        Require(saved.Id != Guid.Empty, "Saved ID must not be empty");
        var restarted = new CalculationStore(environment);
        var restored = await restarted.GetAsync(saved.Id);
        Require(restored != null && restored.Input.Height == 6 && restored.Input.MaterialFlow == 1.66,
            "A new store instance must read persisted input");

        var concurrent = await Task.WhenAll(Enumerable.Range(0, 12)
            .Select(i => store.SaveAsync($"Parallel {i}", new HeatExchangeInput { Height = i + 1 })));
        var afterWrites = await new CalculationStore(environment).ListAsync();
        Require(afterWrites.Count == 13, "Parallel saves must not lose variants");
        Require(afterWrites.Select(x => x.Id).Distinct().Count() == 13, "Saved IDs must be unique");
        Require(concurrent.All(x => afterWrites.Any(y => x.Id == y.Id && x.Input.Height == y.Input.Height)),
            "Every concurrent save must persist its own input");
        Require(await store.DeleteAsync(saved.Id), "Existing variant should be deleted");
        Require(!await store.DeleteAsync(saved.Id), "Deleting an absent variant should return false");
        var afterDelete = new CalculationStore(environment);
        Require(await afterDelete.GetAsync(saved.Id) == null, "Deletion must survive restart");
        Require((await afterDelete.ListAsync()).Count == 12, "Deletion must leave other variants intact");
    }
    finally { DeleteTestDirectory(directory); }
}

static async Task CorruptStorage()
{
    var directory = TestDirectory();
    try
    {
        var store = new CalculationStore(new TestEnvironment(directory));
        var file = Path.Combine(directory, "App_Data", "calculations.json");
        const string damaged = "{broken-json-with-existing-user-data";
        await File.WriteAllTextAsync(file, damaged);
        try { await store.ListAsync(); throw new Exception("Corrupt JSON was accepted during read"); }
        catch (JsonException) { }
        try { await store.SaveAsync("Must not overwrite", new()); throw new Exception("Corrupt JSON was overwritten during save"); }
        catch (JsonException) { }
        Require(await File.ReadAllTextAsync(file) == damaged, "Existing corrupt file must remain unchanged");
        Require(!Directory.EnumerateFiles(Path.GetDirectoryName(file)!, "*.tmp").Any(), "No unfinished temp file should remain");
    }
    finally { DeleteTestDirectory(directory); }
}

static string TestDirectory()
{
    var directory = Path.Combine(Path.GetTempPath(), "TeploobmenSelfTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(directory);
    return directory;
}

static void DeleteTestDirectory(string directory)
{
    var root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "TeploobmenSelfTests")) + Path.DirectorySeparatorChar;
    var resolved = Path.GetFullPath(directory);
    if (!resolved.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("Refusing to remove a directory outside this test's workspace.");
    Directory.Delete(resolved, recursive: true);
}

sealed class TestEnvironment(string contentRootPath) : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "Teploobmen.Tests";
    public string EnvironmentName { get; set; } = "Development";
    public string ContentRootPath { get; set; } = contentRootPath;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = contentRootPath;
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
}
