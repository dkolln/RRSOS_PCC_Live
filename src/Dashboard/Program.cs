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
builder.Services.AddSingleton<DashboardSettings>();
builder.Services.AddSingleton<ItemCatalog>();
builder.Services.AddSingleton<IconCatalog>();
builder.Services.AddSingleton<ProducerDefaults>();
builder.Services.AddSingleton<BaseNames>();
builder.Services.AddSingleton<PlanetNames>();
// The boneyards come from the newest save, read again whenever the game writes it (checked every 10 seconds).
builder.Services.AddSingleton<SaveLooseService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<SaveLooseService>());
builder.Services.AddSingleton<WorldFileService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<WorldFileService>());
builder.Services.AddSingleton<NotebookService>();
// Each recipe pinned in the game goes on the shopping list (from the plugin's pins.json).
builder.Services.AddSingleton<ShoppingListService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ShoppingListService>());

// Cheats page: save-file editing while the game is not in a world (see docs/contract.md's read-only principle —
// this never touches the running game, only the save on disk).
builder.Services.AddSingleton<ResupplyConfigStore>();
builder.Services.AddSingleton<SaveResupplyService>();
builder.Services.AddScoped<ToastService>();
// Base Building: platform templates kept beside the plugin's files, and the read-only reader of beacons and row plans.
builder.Services.AddSingleton<BuildTemplateStore>();
builder.Services.AddSingleton<BaseBuildingService>();
builder.Services.AddSingleton<FactoryService>();
builder.Services.AddSingleton<PlayerTravelService>();
builder.Services.AddSingleton<StorageService>();
// Reads the newest saves ahead of the Storage tab whenever the game is not in a world.
builder.Services.AddHostedService<StorageWarmupService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStaticFiles();
app.UseAntiforgery();
app.MapStaticAssets();

// The icons the plugin wrote out of the game (plugin 0.12.0), one PNG per group id, beside the live files. A missing one is a plain 404.
app.MapGet("/icons/{file}", (string file, IConfiguration config) =>
{
    var path = Path.Combine(LivePaths.Folder(config), "icons", file);
    return file.EndsWith(".png", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(file) == file && File.Exists(path)
        ? Results.File(path, "image/png")
        : Results.NotFound();
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
