using System.Reflection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Transactatrack.Infrastructure.Llm;
using Transactatrack.Infrastructure.Persistence;

namespace Transactatrack.Api.Controllers;

[ApiController]
[Route("api/status")]
public class HealthController : ControllerBase
{
    private static readonly string AssemblyVersion =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "0.0.0";

    private readonly AppDbContext _db;
    private readonly OllamaClient _ollama;
    private readonly ILogger<HealthController> _log;

    public HealthController(AppDbContext db, OllamaClient ollama, ILogger<HealthController> log)
    {
        _db = db;
        _ollama = ollama;
        _log = log;
    }

    [HttpGet]
    public async Task<HealthResponse> Get(CancellationToken ct)
    {
        var dbTask = CheckDatabaseAsync(ct);
        var ollamaTask = CheckOllamaAsync(ct);
        await Task.WhenAll(dbTask, ollamaTask);

        return new HealthResponse(
            new ApiHealth("ok", AssemblyVersion),
            await dbTask,
            await ollamaTask);
    }

    private async Task<DatabaseHealth> CheckDatabaseAsync(CancellationToken ct)
    {
        try
        {
            var ok = await _db.Database.CanConnectAsync(ct);
            return ok ? new DatabaseHealth("ok", null) : new DatabaseHealth("error", "CanConnectAsync returned false");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Database health check failed");
            return new DatabaseHealth("error", ex.Message);
        }
    }

    private async Task<OllamaHealth> CheckOllamaAsync(CancellationToken ct)
    {
        try
        {
            var models = await _ollama.GetTagsAsync(ct);
            return new OllamaHealth("ok", models, null);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Ollama health check failed");
            return new OllamaHealth("error", [], ex.Message);
        }
    }
}

public record HealthResponse(ApiHealth Api, DatabaseHealth Database, OllamaHealth Ollama);
public record ApiHealth(string Status, string Version);
public record DatabaseHealth(string Status, string? Message);
public record OllamaHealth(string Status, IReadOnlyList<string> Models, string? Message);
