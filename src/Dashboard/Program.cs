using RRSOS.PCC.Dashboard;
using RRSOS.PCC.Dashboard.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// The one data source: the plugin's live file. It works whichever of the game and this app starts first.
builder.Services.AddSingleton<LiveFileService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LiveFileService>());
builder.Services.AddSingleton<PCLauncherService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
