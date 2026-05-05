using Microsoft.Extensions.Diagnostics.HealthChecks;
using Transactatrack.Infrastructure.Llm;

namespace Transactatrack.Api.HealthChecks;

public class OllamaHealthCheck(OllamaClient ollama) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            var models = await ollama.GetTagsAsync(ct);
            return HealthCheckResult.Healthy(data: new Dictionary<string, object> { ["models"] = models });
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(ex.Message, ex);
        }
    }
}
