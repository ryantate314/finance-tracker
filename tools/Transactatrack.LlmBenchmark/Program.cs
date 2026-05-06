using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Transactatrack.Application.Categorization;
using Transactatrack.Infrastructure.Categorization;
using Transactatrack.Infrastructure.Llm;
using Transactatrack.LlmBenchmark;

var options = BenchmarkOptions.Parse(args);

if (options.ShowHelp)
{
    BenchmarkOptions.PrintHelp();
    return 0;
}

var host = Host.CreateDefaultBuilder()
    .UseContentRoot(AppContext.BaseDirectory)
    .ConfigureAppConfiguration((_, cfg) =>
    {
        var overrides = new Dictionary<string, string?>();
        if (options.Model   is not null) overrides["Ollama:Model"]   = options.Model;
        if (options.BaseUrl is not null) overrides["Ollama:BaseUrl"] = options.BaseUrl;
        if (overrides.Count > 0) cfg.AddInMemoryCollection(overrides);
    })
    .ConfigureServices((ctx, services) =>
    {
        var config = ctx.Configuration;
        services.AddHttpClient<OllamaClient>(c =>
        {
            var baseUrl = config["Ollama:BaseUrl"]
                ?? throw new InvalidOperationException(
                    "Ollama:BaseUrl is not configured. Set it in appsettings.json or pass --base-url.");
            c.BaseAddress = new Uri(baseUrl);
        });
        services.AddScoped<IOllamaCategorizer, OllamaCategorizer>();
        services.AddScoped<BenchmarkRunner>();
    })
    .ConfigureLogging(logging =>
    {
        logging.ClearProviders();
        logging.AddConsole();
        logging.AddFilter("Microsoft", LogLevel.None);
        logging.AddFilter("System",    LogLevel.None);
        logging.SetMinimumLevel(options.Verbose ? LogLevel.Debug : LogLevel.Warning);
    })
    .Build();

await using var scope = host.Services.CreateAsyncScope();
var runner = scope.ServiceProvider.GetRequiredService<BenchmarkRunner>();
var result = await runner.RunAsync(options);

BenchmarkReport.Print(result, options);

if (options.Output is not null)
    await BenchmarkReport.WriteJsonAsync(result, options.Output);

return 0;

// ── CLI arg parser ───────────────────────────────────────────────────────────

public class BenchmarkOptions
{
    public string InputPath { get; init; } = Path.Combine(AppContext.BaseDirectory, "sample-data.json");
    public string? Model    { get; init; }
    public string? BaseUrl  { get; init; }
    public int    Runs      { get; init; } = 1;
    public int    BatchSize { get; init; } = 5;
    public string? Output   { get; init; }
    public bool   Verbose   { get; init; }
    public bool   ShowHelp  { get; init; }

    public static BenchmarkOptions Parse(string[] args)
    {
        string? input = null, model = null, baseUrl = null, output = null;
        int runs = 1, batchSize = 5;
        bool verbose = false, help = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--input"      when i + 1 < args.Length: input    = args[++i]; break;
                case "--model"      when i + 1 < args.Length: model    = args[++i]; break;
                case "--base-url"   when i + 1 < args.Length: baseUrl  = args[++i]; break;
                case "--runs"       when i + 1 < args.Length: runs      = int.Parse(args[++i]); break;
                case "--batch-size" when i + 1 < args.Length: batchSize = int.Parse(args[++i]); break;
                case "--output"     when i + 1 < args.Length: output   = args[++i]; break;
                case "--verbose": verbose = true; break;
                case "--help":
                case "-h":         help = true; break;
            }
        }

        return new BenchmarkOptions
        {
            InputPath = input ?? Path.Combine(AppContext.BaseDirectory, "sample-data.json"),
            Model     = model,
            BaseUrl   = baseUrl,
            Runs      = runs,
            BatchSize = batchSize,
            Output    = output,
            Verbose   = verbose,
            ShowHelp  = help,
        };
    }

    public static void PrintHelp()
    {
        Console.WriteLine("Usage: Transactatrack.LlmBenchmark [options]");
        Console.WriteLine();
        Console.WriteLine("  --input <path>      Labeled data JSON (default: sample-data.json next to the exe)");
        Console.WriteLine("  --model <name>      Override Ollama:Model from appsettings.json");
        Console.WriteLine("  --base-url <url>    Override Ollama:BaseUrl from appsettings.json");
        Console.WriteLine("  --runs <n>          Repeat each transaction n times for stability (default: 1)");
        Console.WriteLine("  --batch-size <n>    Batch size for SuggestAsync calls (default: 5)");
        Console.WriteLine("  --output <path>     Write JSON report dump to path");
        Console.WriteLine("  --verbose           Print every row's prediction, not just misses");
        Console.WriteLine("  --help              Show this message");
    }
}
