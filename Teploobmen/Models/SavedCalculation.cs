namespace Teploobmen.Models;

public sealed record SavedCalculation(Guid Id, string Name, DateTimeOffset SavedAt, HeatExchangeInput Input);
