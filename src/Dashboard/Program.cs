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
builder.Services.AddSingleton<FoldState>();
builder.Services.AddSingleton<TerraformersService>();
builder.Services.AddSingleton<ItemCatalog>();
builder.Services.AddSingleton<IconCatalog>();
builder.Services.AddSingleton<BaseNames>();
builder.Services.AddSingleton<PlanetNames>();
builder.Services.AddSingleton<WorldFileService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<WorldFileService>());
builder.Services.AddSingleton<NotebookService>();
// Each recipe pinned in the game goes on the shopping list (from the plugin's pins.json).
builder.Services.AddSingleton<ShoppingListService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<ShoppingListService>());

builder.Services.AddScoped<ToastService>();
builder.Services.AddSingleton<FactoryService>();

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
