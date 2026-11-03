using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Teploobmen.Infrastructure;

/// <summary>Both 0,72 and 0.72 are valid decimal inputs; thousands separators are not accepted.</summary>
public sealed class FlexibleDoubleModelBinder : IModelBinder
{
    public Task BindModelAsync(ModelBindingContext context)
    {
        var value = context.ValueProvider.GetValue(context.ModelName);
        if (value == ValueProviderResult.None) return Task.CompletedTask;
        context.ModelState.SetModelValue(context.ModelName, value);
        var raw = value.FirstValue?.Trim().Replace(',', '.');
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && double.IsFinite(parsed))
            context.Result = ModelBindingResult.Success(parsed);
        else context.ModelState.TryAddModelError(context.ModelName, "Введите конечное число, например 0,72.");
        return Task.CompletedTask;
    }
}

public sealed class FlexibleDoubleModelBinderProvider : IModelBinderProvider
{
    public IModelBinder? GetBinder(ModelBinderProviderContext context) =>
        context.Metadata.ModelType == typeof(double) ? new FlexibleDoubleModelBinder() : null;
}
