# LLM categorization benchmark CLI

## Context

The new Phase-3 LLM categorization (Ollama-backed `OllamaCategorizer`) is in production but has no way to measure how well it actually works. The output is non-deterministic, so a traditional unit/integration test is the wrong shape — we want a *measurement* harness that runs labeled transactions through the real Ollama stack and reports accuracy, confidence calibration, and per-category breakdowns.

The deliverable is a standalone .NET console app at `tools/Transactatrack.LlmBenchmark/` that:

1. Loads a JSON file of labeled transactions (description, merchant, amount, date, expected category/sub-category names) plus the category tree to use for the run.
2. Constructs the real `OllamaCategorizer` with the existing DI wiring pattern (`AddHttpClient<OllamaClient>` against the configured `Ollama:BaseUrl`/`Ollama:Model`).
3. Calls `SuggestAsync` in batches and compares each result to the expected label.
4. Prints a console report (overall accuracy, sub-category accuracy, confidence calibration, per-category precision/recall, a misclassification list) and optionally writes a JSON dump for run-to-run diffs.

The tool does **not** touch the database, has no auth, and is invoked manually — it is not part of `make test`. This keeps the deterministic test suite clean while giving a sharp tool for prompt iteration and model comparison.

## Critical files

### New project
- `tools/Transactatrack.LlmBenchmark/Transactatrack.LlmBenchmark.csproj` — net10.0 console app.
  - References: `src/Transactatrack.Application/Transactatrack.Application.csproj`, `src/Transactatrack.Infrastructure/Transactatrack.Infrastructure.csproj`, `src/Transactatrack.Domain/Transactatrack.Domain.csproj`.
  - Packages: `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Configuration.Json`, `Microsoft.Extensions.Http`, `Microsoft.Extensions.Logging.Console`. (All already pulled in transitively by the API project, so versions should match what's already in the lockfile.)
- `tools/Transactatrack.LlmBenchmark/Program.cs` — entry point. Parses args, builds host, runs `BenchmarkRunner`, prints report, returns exit code.
- `tools/Transactatrack.LlmBenchmark/BenchmarkRunner.cs` — orchestrates the loop: load JSON, build in-memory `Category`/`SubCategory` lists, build in-memory `Transaction` list, call `IOllamaCategorizer.SuggestAsync` in `LlmBatchSize`-sized batches, accumulate metrics.
- `tools/Transactatrack.LlmBenchmark/BenchmarkData.cs` — POCOs for the input file (`BenchmarkInput`, `LabeledTransaction`, `BenchmarkCategory`, `BenchmarkSubCategory`).
- `tools/Transactatrack.LlmBenchmark/BenchmarkReport.cs` — metric aggregation + console formatter. Plain ASCII table, no extra deps.
- `tools/Transactatrack.LlmBenchmark/sample-data.json` — small starter dataset (~10 hand-labeled rows) so the tool runs out-of-the-box. Gitignored variant `local-data.json` is the working set the user iterates on.
- `tools/Transactatrack.LlmBenchmark/appsettings.json` — minimal: `{ "Ollama": { "BaseUrl": "http://localhost:11434", "Model": "llama3.2:1b" } }`. Mirrors the API's `appsettings.json` keys so the `OllamaClient` ctor (`configuration["Ollama:Model"]`) and the `AddHttpClient` lambda (`Ollama:BaseUrl`) both resolve. Overridable via `--model` and `--base-url` CLI args.

### Solution + Makefile
- `Transactatrack.slnx` — add the new project entry alongside the existing six.
- `Makefile` — add a `bench` target: `dotnet run --project tools/Transactatrack.LlmBenchmark -- $(ARGS)` so `make bench ARGS="--input local-data.json"` works.

### No edits to existing source
- `OllamaClient`, `OllamaCategorizer`, `Category`, `SubCategory`, `Transaction` are reused as-is. No changes to `src/`.

## Reusable existing code (verified paths)

- `src/Transactatrack.Infrastructure/Llm/OllamaClient.cs` — ctor `(HttpClient http, IConfiguration configuration)`. Reads `Ollama:Model` (default `llama3.2:1b`).
- `src/Transactatrack.Infrastructure/Categorization/OllamaCategorizer.cs` — ctor `(OllamaClient ollama, ILogger<OllamaCategorizer> logger)`. Public method:
  ```csharp
  Task<IDictionary<Guid, LlmCategorizationResult>> SuggestAsync(
      IReadOnlyList<Transaction> transactions,
      IReadOnlyList<Category> categories,
      IReadOnlyList<SubCategory> subCategories,
      CancellationToken ct);
  ```
- `src/Transactatrack.Application/Categorization/IOllamaCategorizer.cs` — `LlmCategorizationResult(Guid CategoryId, Guid? SubCategoryId, decimal Confidence, string Model)`.
- DI pattern to mirror (from `src/Transactatrack.Api/Program.cs:32-49`):
  ```csharp
  services.AddHttpClient<OllamaClient>(c =>
  {
      var baseUrl = config["Ollama:BaseUrl"] ?? throw new InvalidOperationException("…");
      c.BaseAddress = new Uri(baseUrl);
  });
  services.AddSingleton<IOllamaCategorizer, OllamaCategorizer>();
  ```
  (Singleton instead of Scoped — the benchmark host has no scoped lifetime.)

## Input file format

```json
{
  "categories": [
    { "name": "Food", "subCategories": ["Groceries", "Restaurants"] },
    { "name": "Transport", "subCategories": ["Gas", "Public Transit"] },
    { "name": "Shopping", "subCategories": [] }
  ],
  "transactions": [
    {
      "date": "2026-04-15",
      "amount": -42.10,
      "description": "AMAZON.COM PURCHASE",
      "merchant": "Amazon",
      "expectedCategory": "Shopping",
      "expectedSubCategory": null
    }
  ]
}
```

`BenchmarkRunner` builds in-memory `Category`/`SubCategory` instances with synthesized `Guid.NewGuid()` IDs and a single fixed `FamilyId` (the LLM never sees these GUIDs — they're internal). Expected labels are matched by *name* against the returned IDs through a dictionary the runner builds locally.

## Metrics computed

For N transactions × R runs (default R=1, configurable via `--runs` for stability sampling):

- **Category accuracy** — `correct_category / total`
- **Sub-category accuracy** — only over transactions whose `expectedSubCategory != null`: `correct_sub / eligible`
- **Refusal rate** — `omitted_by_llm / total` (the categorizer drops low-confidence rows)
- **Confidence calibration** — mean confidence of correct vs incorrect predictions (sanity check that high confidence ≠ random)
- **Per-category** — precision and recall for each expected category
- **Misclassifications list** — for each wrong row: description, expected, got, confidence

Console output is two ASCII tables (overall + per-category) plus an optional `--output report.json` dump for diffing across model/prompt iterations.

## CLI surface

```
Transactatrack.LlmBenchmark [options]
  --input <path>      Labeled data JSON (default: tools/Transactatrack.LlmBenchmark/sample-data.json)
  --model <name>      Override Ollama:Model (default: from appsettings.json)
  --base-url <url>    Override Ollama:BaseUrl
  --runs <n>          Repeat each transaction n times, average (default: 1)
  --batch-size <n>    Batch size for SuggestAsync calls (default: 5, matches CategorizationService.LlmBatchSize)
  --output <path>     Optional JSON report dump
  --verbose           Print every row's prediction (not just misses)
```

Parsing is hand-rolled — no `System.CommandLine` dependency. The arg surface is small.

## Verification

1. **Build** — `dotnet build tools/Transactatrack.LlmBenchmark/Transactatrack.LlmBenchmark.csproj` succeeds with zero warnings. `dotnet build` of the solution still succeeds (slnx entry valid).
2. **Smoke run** — with Ollama running locally (`make api` proves the model is up), `make bench` against the bundled `sample-data.json` completes and prints a table. Exit code 0. The numbers are not asserted — this is a measurement tool, not a pass/fail test.
3. **Argument overrides** — `make bench ARGS="--model llama3.2:1b --runs 2 --verbose"` shows two runs per row, prints all predictions, uses the specified model.
4. **JSON output** — `make bench ARGS="--output /tmp/run1.json"` produces a JSON file with the metrics; spot-check the structure.
5. **Existing test suites unchanged** — `make test-unit` and `make test-integration` still pass (no `src/` edits, so this is a regression sanity check rather than a verification of the new code).
6. **No DB / no API needed** — the tool runs cleanly without `make api` or `make db-update` because it doesn't touch Postgres. The only external dependency is Ollama on `localhost:11434`.
