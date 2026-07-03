using PredictiveAnalysis.Components;
using PredictiveAnalysis.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Analysis pipeline: data + rules engine + ML model + Semantic Kernel narratives are
// singletons (they hold the trained model / kernel); the orchestrator is scoped.
builder.Services.AddSingleton<DataRepository>();
builder.Services.AddSingleton<RiskRulesEngine>();
builder.Services.AddSingleton<RiskModelTrainer>();
builder.Services.AddSingleton<NarrativeService>();
builder.Services.AddScoped<PredictiveAnalysisService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
