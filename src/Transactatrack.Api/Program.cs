using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Transactatrack.Api.HealthChecks;
using Transactatrack.Api.Middleware;
using Transactatrack.Application;
using Transactatrack.Application.Categorization;
using Transactatrack.Application.Imports;
using Transactatrack.Infrastructure.Categorization;
using Transactatrack.Infrastructure.Imports;
using Transactatrack.Infrastructure.Imports.Parsers;
using Transactatrack.Infrastructure.Llm;
using Transactatrack.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console());

builder.Services.AddControllers()
    .AddJsonOptions(opt =>
        opt.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
builder.Services.AddOpenApi();

builder.Services.AddScoped<FamilyContext>();
builder.Services.AddScoped<IFamilyContext>(sp => sp.GetRequiredService<FamilyContext>());

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddHttpClient<OllamaClient>(c =>
{
    var baseUrl = builder.Configuration["Ollama:BaseUrl"]
        ?? throw new InvalidOperationException("Ollama:BaseUrl is not configured");
    c.BaseAddress = new Uri(baseUrl);
});

builder.Services.AddHealthChecks()
    .AddCheck<DatabaseHealthCheck>("database", tags: ["ready"])
    .AddCheck<OllamaHealthCheck>("ollama", tags: ["ready"]);

builder.Services.AddSingleton<IBankCsvParser, ChaseParser>();
builder.Services.AddSingleton<IBankParserRegistry, BankParserRegistry>();
builder.Services.AddSingleton<SourceRowHasher>();
builder.Services.AddScoped<IImportService, ImportService>();

builder.Services.AddScoped<IRuleEngine, RuleEngine>();
builder.Services.AddScoped<IOllamaCategorizer, OllamaCategorizer>();
builder.Services.AddScoped<ICategorizationService, CategorizationService>();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(o => o.AddPolicy("AngularDev", p => p
        .WithOrigins("http://localhost:4200")
        .AllowAnyHeader()
        .AllowAnyMethod()));
}

var app = builder.Build();

if (builder.Configuration.GetValue<bool>("Database:AutoMigrate"))
{
    using var scope = app.Services.CreateScope();
    AppDbContext db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors("AngularDev");
}

app.UseSerilogRequestLogging();
app.UseMiddleware<FamilyContextMiddleware>();
app.MapControllers();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = c => c.Tags.Contains("ready") });

app.Run();

public partial class Program { }
