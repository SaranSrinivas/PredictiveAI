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

// Warm up the rules engine (first-call rule compilation) and the ML.NET model (training) at
// startup, so that cost is paid once here rather than by whichever user's browser loads
// /dashboard first.
using (var warmupScope = app.Services.CreateScope())
{
    await warmupScope.ServiceProvider.GetRequiredService<PredictiveAnalysisService>().AnalyzeAsync();
}

// Diagnostic: list all endpoints at startup to detect route conflicts
try
{
    var endpointDataSource = app.Services.GetService(typeof(Microsoft.AspNetCore.Routing.EndpointDataSource)) as Microsoft.AspNetCore.Routing.EndpointDataSource;
    if (endpointDataSource != null)
    {
        Console.WriteLine("Registered endpoints:");
        foreach (var ep in endpointDataSource.Endpoints)
        {
            Console.WriteLine(ep.DisplayName ?? ep.ToString());
            var pattern = (ep as Microsoft.AspNetCore.Routing.RouteEndpoint)?.RoutePattern?.RawText;
            if (!string.IsNullOrEmpty(pattern))
            {
                Console.WriteLine($"  Pattern: {pattern}");
            }
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Endpoint listing failed: {ex.Message}");
}

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
