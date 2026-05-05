using Microsoft.EntityFrameworkCore;
using Serilog;
using Transactatrack.Infrastructure.Llm;
using Transactatrack.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console());

builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddHttpClient<OllamaClient>(c =>
{
    var baseUrl = builder.Configuration["Ollama:BaseUrl"]
        ?? throw new InvalidOperationException("Ollama:BaseUrl is not configured");
    c.BaseAddress = new Uri(baseUrl);
});

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(o => o.AddPolicy("AngularDev", p => p
        .WithOrigins("http://localhost:4200")
        .AllowAnyHeader()
        .AllowAnyMethod()));
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseCors("AngularDev");
}

app.UseSerilogRequestLogging();
app.MapControllers();

app.Run();
