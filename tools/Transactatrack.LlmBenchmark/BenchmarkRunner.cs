using System.Text.Json;
using Microsoft.Extensions.Logging;
using Transactatrack.Application.Categorization;
using Transactatrack.Domain.Entities;

namespace Transactatrack.LlmBenchmark;

public class BenchmarkRunner
{
    private readonly IOllamaCategorizer _categorizer;
    private readonly ILogger<BenchmarkRunner> _logger;

    public BenchmarkRunner(IOllamaCategorizer categorizer, ILogger<BenchmarkRunner> logger)
    {
        _categorizer = categorizer;
        _logger = logger;
    }

    public async Task<BenchmarkResult> RunAsync(BenchmarkOptions options, CancellationToken ct = default)
    {
        var input = await LoadInputAsync(options.InputPath, ct);
        Console.WriteLine($"Loaded {input.Transactions.Count} transactions across {input.Categories.Count} categories");

        var familyId = Guid.NewGuid();
        var categories    = BuildCategories(input.Categories, familyId);
        var subCategories = BuildSubCategories(input.Categories, categories, familyId);

        var categoryById    = categories.ToDictionary(c => c.Id, c => c.Name);
        var subCategoryById = subCategories.ToDictionary(sc => sc.Id, sc => sc.Name);
        var categoryByName    = categories.ToDictionary(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase);
        var subCategoryByName = subCategories.ToDictionary(sc => sc.Name, sc => sc.Id, StringComparer.OrdinalIgnoreCase);

        string model = "unknown";
        var allPredictions = new List<Prediction>();

        for (int run = 1; run <= options.Runs; run++)
        {
            if (options.Runs > 1)
                Console.WriteLine($"  Run {run}/{options.Runs}...");

            var txMap = new Dictionary<Guid, LabeledTransaction>();
            var transactions = input.Transactions.Select(lt =>
            {
                var tx = new Transaction
                {
                    Id             = Guid.NewGuid(),
                    FamilyId       = familyId,
                    Date           = DateTime.Parse(lt.Date),
                    Amount         = lt.Amount,
                    Description    = lt.Description,
                    Merchant       = lt.Merchant,
                    AccountId      = Guid.Empty,
                    ImportBatchId  = Guid.Empty,
                    SourceRowHash  = string.Empty,
                    CreatedUtc     = DateTime.UtcNow,
                };
                txMap[tx.Id] = lt;
                return tx;
            }).ToList();

            for (int i = 0; i < transactions.Count; i += options.BatchSize)
            {
                var batch = transactions.GetRange(i, Math.Min(options.BatchSize, transactions.Count - i));
                _logger.LogDebug("Batch {Start}–{End} / {Total}", i + 1, i + batch.Count, transactions.Count);

                var results = await _categorizer.SuggestAsync(batch, categories, subCategories, ct);

                foreach (var tx in batch)
                {
                    var labeled = txMap[tx.Id];
                    var expectedCatId = categoryByName.GetValueOrDefault(labeled.ExpectedCategory);
                    var expectedSubId = labeled.ExpectedSubCategory is not null
                        ? subCategoryByName.GetValueOrDefault(labeled.ExpectedSubCategory)
                        : (Guid?)null;

                    if (results.TryGetValue(tx.Id, out var llm))
                    {
                        if (model == "unknown") model = llm.Model;

                        allPredictions.Add(new Prediction(
                            Run:               run,
                            Description:       tx.Description,
                            ExpectedCategory:  labeled.ExpectedCategory,
                            ExpectedSubCategory: labeled.ExpectedSubCategory,
                            PredictedCategory: categoryById.GetValueOrDefault(llm.CategoryId, "Unknown"),
                            PredictedSubCategory: llm.SubCategoryId is not null
                                ? subCategoryById.GetValueOrDefault(llm.SubCategoryId.Value)
                                : null,
                            Confidence:        llm.Confidence,
                            Refused:           false,
                            CategoryCorrect:   llm.CategoryId == expectedCatId,
                            SubCategoryCorrect: labeled.ExpectedSubCategory is not null
                                && llm.SubCategoryId == expectedSubId
                        ));
                    }
                    else
                    {
                        allPredictions.Add(new Prediction(
                            Run:               run,
                            Description:       tx.Description,
                            ExpectedCategory:  labeled.ExpectedCategory,
                            ExpectedSubCategory: labeled.ExpectedSubCategory,
                            PredictedCategory: "[refused]",
                            PredictedSubCategory: null,
                            Confidence:        0,
                            Refused:           true,
                            CategoryCorrect:   false,
                            SubCategoryCorrect: false
                        ));
                    }
                }
            }
        }

        return Aggregate(allPredictions, model, input.Transactions.Count, options.Runs);
    }

    private static BenchmarkResult Aggregate(
        List<Prediction> predictions, string model, int txCount, int runs)
    {
        var refused  = predictions.Where(p => p.Refused).ToList();
        var answered = predictions.Where(p => !p.Refused).ToList();

        var correctCat = answered.Count(p => p.CategoryCorrect);
        var eligibleSub = answered.Count(p => p.ExpectedSubCategory is not null);
        var correctSub  = answered.Count(p => p.SubCategoryCorrect);

        var correct   = answered.Where(p => p.CategoryCorrect).ToList();
        var incorrect = answered.Where(p => !p.CategoryCorrect).ToList();
        var meanConfCorrect   = correct.Count   > 0 ? correct.Average(p => (double)p.Confidence)   : 0;
        var meanConfIncorrect = incorrect.Count > 0 ? incorrect.Average(p => (double)p.Confidence) : 0;

        var categoryNames = predictions.Select(p => p.ExpectedCategory).Distinct().OrderBy(n => n).ToList();
        var perCategory = categoryNames.Select(cat => new CategoryMetrics(
            cat,
            TP: answered.Count(p => p.ExpectedCategory == cat && p.CategoryCorrect),
            FP: answered.Count(p => p.PredictedCategory == cat && p.ExpectedCategory != cat),
            FN: predictions.Count(p => p.ExpectedCategory == cat && !p.CategoryCorrect)
        )).ToList();

        var misses = answered.Where(p => !p.CategoryCorrect)
            .Concat(refused)
            .Select(p => new Misclassification(
                p.Description, p.ExpectedCategory, p.ExpectedSubCategory,
                p.PredictedCategory, p.PredictedSubCategory, p.Confidence))
            .ToList();

        return new BenchmarkResult(
            Model:                  model,
            TotalTransactions:      txCount,
            TotalRuns:              runs,
            AnsweredCount:          answered.Count,
            CorrectCategory:        correctCat,
            CorrectSubCategory:     correctSub,
            EligibleSubCategory:    eligibleSub,
            RefusedCount:           refused.Count,
            MeanConfidenceCorrect:  meanConfCorrect,
            MeanConfidenceIncorrect: meanConfIncorrect,
            PerCategory:            perCategory,
            Misclassifications:     misses,
            AllPredictions:         predictions
        );
    }

    private static List<Category> BuildCategories(List<BenchmarkCategory> benchmarkCats, Guid familyId) =>
        benchmarkCats.Select(bc => new Category
        {
            Id = Guid.NewGuid(), FamilyId = familyId, Name = bc.Name, CreatedUtc = DateTime.UtcNow,
        }).ToList();

    private static List<SubCategory> BuildSubCategories(
        List<BenchmarkCategory> benchmarkCats, List<Category> categories, Guid familyId)
    {
        var result = new List<SubCategory>();
        for (int i = 0; i < benchmarkCats.Count; i++)
            foreach (var name in benchmarkCats[i].SubCategories)
                result.Add(new SubCategory
                {
                    Id = Guid.NewGuid(), FamilyId = familyId,
                    CategoryId = categories[i].Id, Name = name, CreatedUtc = DateTime.UtcNow,
                });
        return result;
    }

    private static async Task<BenchmarkInput> LoadInputAsync(string path, CancellationToken ct)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Input file not found: {path}");

        await using var stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<BenchmarkInput>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            ct
        ) ?? throw new InvalidOperationException($"Failed to parse {path}");
    }
}
