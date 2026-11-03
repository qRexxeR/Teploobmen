using Teploobmen.Infrastructure;
using Teploobmen.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllersWithViews(options =>
    options.ModelBinderProviders.Insert(0, new FlexibleDoubleModelBinderProvider()));
builder.Services.AddSingleton<HeatExchangeCalculator>();
builder.Services.AddSingleton<CalculationStore>();

var app = builder.Build();
if (!app.Environment.IsDevelopment()) app.UseExceptionHandler("/Calculator/Error");
app.UseStaticFiles();
app.UseRouting();
app.MapControllerRoute("default", "{controller=Calculator}/{action=Index}/{id?}");
app.Run();
