using MiniController.Web.Components;
using MiniController.Web.Scheduling;
using MiniController.Web.Services;
using MiniController.Web.Systems;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// App-wide display preferences (temperature unit, etc.).
builder.Services.AddSingleton<AppPreferences>();

// Mini-split (climate) backend.
builder.Services.AddSingleton<DeviceManager>();

// System registry — each controllable system registers as an ISystemModule and
// the rail, dashboard, and poller build themselves from this set. Add new
// systems here as they're implemented.
builder.Services.AddSingleton<ISystemModule, ClimateModule>();
builder.Services.AddSingleton<ISystemRegistry, SystemRegistry>();

builder.Services.AddHostedService<StatusPollingService>();

// Climate scheduler.
builder.Services.AddSingleton<ScheduleStore>();
builder.Services.AddHostedService<ScheduleRunner>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
