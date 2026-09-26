using RRSOS.PCC.Dashboard;
using RRSOS.PCC.Dashboard.Components;

var builder = WebApplication.CreateBuilder(args);

// This PC's own settings (see appsettings.json for the list), kept out of git. The command line still wins over them.
builder.Configuration.AddJsonFile(Path.Combine(builder.Environment.ContentRootPath, "appsettings.Local.json"), optional: true);
builder.Configuration.AddCommandLine(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// The one data source: the plugin's live file. It works whichever of the game and this app starts first.
builder.Services.AddSingleton<LiveFileService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LiveFileService>());
builder.Services.AddSingleton<PCLauncherService>();

// Spoken alerts, in the default voice set in Windows.
builder.Services.AddSingleton<SpeechService>();

// The slower second file (bases, containers, extractors), plus what it needs: an item catalog and the base names.
builder.Services.AddSingleton<ItemCatalog>();
builder.Services.AddSingleton<BaseNames>();
// The boneyards come from the newest save, read again whenever the game writes it (checked every 10 seconds).
builder.Services.AddSingleton<SaveLooseService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SaveLooseService>());
builder.Services.AddSingleton<WorldFileService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<WorldFileService>());
builder.Services.AddSingleton<NotebookService>();

// Cheats page: save-file editing while the game is not in a world (see docs/contract.md's read-only principle —
// this never touches the running game, only the save on disk).
builder.Services.AddSingleton<ResupplyConfigStore>();
builder.Services.AddSingleton<SaveResupplyService>();
builder.Services.AddScoped<ToastService>();
// Base Building: platform templates kept beside the plugin's files, and the read-only reader of beacons and row plans.
builder.Services.AddSingleton<BuildTemplateStore>();
builder.Services.AddSingleton<BaseBuildingService>();

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
