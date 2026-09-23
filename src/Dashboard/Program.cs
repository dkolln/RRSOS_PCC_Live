using RRSOS.PCC.Dashboard;
using RRSOS.PCC.Dashboard.Components;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// The one data source: the plugin's live file. It works whichever of the game and this app starts first.
builder.Services.AddSingleton<LiveFileService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<LiveFileService>());
builder.Services.AddSingleton<PCLauncherService>();

// The slower second file (bases, containers, extractors), plus what it needs: an item catalog and the base names.
builder.Services.AddSingleton<ItemCatalog>();
builder.Services.AddSingleton<BaseNames>();
builder.Services.AddSingleton<WorldFileService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<WorldFileService>());
builder.Services.AddSingleton<NotebookService>();

// Cheats page: save-file editing while the game is not in a world (see docs/contract.md's read-only principle —
// this never touches the running game, only the save on disk).
builder.Services.AddSingleton<ResupplyConfigStore>();
builder.Services.AddSingleton<SaveResupplyService>();
builder.Services.AddScoped<ToastService>();

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
