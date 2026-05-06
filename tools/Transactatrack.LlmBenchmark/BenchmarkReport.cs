using System.Text.Json;

namespace Transactatrack.LlmBenchmark;

public record Prediction(
    int Run,
    string Description,
    string ExpectedCategory,
    string? ExpectedSubCategory,
    string PredictedCategory,
    string? PredictedSubCategory,
    decimal Confidence,
    bool Refused,
    bool CategoryCorrect,
    bool SubCategoryCorrect);

public record CategoryMetrics(string Name, int TP, int FP, int FN)
{
    public double Precision => (TP + FP) == 0 ? 0 : (double)TP / (TP + FP);
    public double Recall    => (TP + FN) == 0 ? 0 : (double)TP / (TP + FN);
}

public record Misclassification(
    string Description,
    string ExpectedCategory,
    string? ExpectedSubCategory,
    string GotCategory,
    string? GotSubCategory,
    decimal Confidence);

public record BenchmarkResult(
    string Model,
    int TotalTransactions,
    int TotalRuns,
    int AnsweredCount,
    int CorrectCategory,
    int CorrectSubCategory,
    int EligibleSubCategory,
    int RefusedCount,
    double MeanConfidenceCorrect,
    double MeanConfidenceIncorrect,
    IReadOnlyList<CategoryMetrics> PerCategory,
    IReadOnlyList<Misclassification> Misclassifications,
    IReadOnlyList<Prediction> AllPredictions);

public static class BenchmarkReport
{
    public static void Print(BenchmarkResult r, BenchmarkOptions opts)
    {
        var total = r.TotalTransactions * r.TotalRuns;

        Console.WriteLine();
        Console.WriteLine(new string('=', 62));
        Console.WriteLine($"  LLM Benchmark — {r.Model} — {r.TotalTransactions} tx × {r.TotalRuns} run(s)");
        Console.WriteLine(new string('=', 62));

        Console.WriteLine();
        Console.WriteLine("Overall Metrics");
        PrintRow("Category accuracy",   $"{r.CorrectCategory} / {r.AnsweredCount}",        Pct(r.CorrectCategory, r.AnsweredCount));
        PrintRow("Sub-cat accuracy",    $"{r.CorrectSubCategory} / {r.EligibleSubCategory}", Pct(r.CorrectSubCategory, r.EligibleSubCategory));
        PrintRow("Refusal rate",        $"{r.RefusedCount} / {total}",                       Pct(r.RefusedCount, total));
        PrintRow("Conf (correct)",      $"{r.MeanConfidenceCorrect:F2}");
        PrintRow("Conf (wrong)",        $"{r.MeanConfidenceIncorrect:F2}");

        Console.WriteLine();
        Console.WriteLine("Per-Category");
        Console.WriteLine($"  {"Category",-20} {"TP",4} {"FP",4} {"FN",4}  {"Precision",9}  {"Recall",7}");
        Console.WriteLine($"  {new string('-', 58)}");
        foreach (var cat in r.PerCategory)
            Console.WriteLine($"  {cat.Name,-20} {cat.TP,4} {cat.FP,4} {cat.FN,4}  {cat.Precision,8:P1}  {cat.Recall,6:P1}");

        if (opts.Verbose)
        {
            Console.WriteLine();
            Console.WriteLine("All Predictions");
            Console.WriteLine($"  {"Description",-34} {"Expected",-14} {"Got",-14} {"Conf",5}  {"":4}");
            Console.WriteLine($"  {new string('-', 76)}");
            foreach (var p in r.AllPredictions)
            {
                var ok = p.Refused ? "SKIP" : p.CategoryCorrect ? "OK  " : "MISS";
                Console.WriteLine($"  {Trunc(p.Description, 32),-34} {Trunc(p.ExpectedCategory, 12),-14} {Trunc(p.PredictedCategory, 12),-14} {p.Confidence,5:F2}  {ok}");
            }
        }

        if (r.Misclassifications.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine($"Misclassifications ({r.Misclassifications.Count})");
            Console.WriteLine($"  {"Description",-34} {"Expected",-14} {"Got",-14} {"Conf",5}");
            Console.WriteLine($"  {new string('-', 71)}");
            foreach (var m in r.Misclassifications)
                Console.WriteLine($"  {Trunc(m.Description, 32),-34} {Trunc(m.ExpectedCategory, 12),-14} {Trunc(m.GotCategory, 12),-14} {m.Confidence,5:F2}");
        }

        Console.WriteLine();
    }

    public static async Task WriteJsonAsync(BenchmarkResult r, string path)
    {
        var total = r.TotalTransactions * r.TotalRuns;
        var doc = new
        {
            timestamp          = DateTime.UtcNow,
            model              = r.Model,
            totalTransactions  = r.TotalTransactions,
            totalRuns          = r.TotalRuns,
            categoryAccuracy   = r.AnsweredCount > 0 ? (double)r.CorrectCategory / r.AnsweredCount : 0,
            subCategoryAccuracy = r.EligibleSubCategory > 0 ? (double)r.CorrectSubCategory / r.EligibleSubCategory : 0,
            refusalRate        = total > 0 ? (double)r.RefusedCount / total : 0,
            meanConfidenceCorrect   = r.MeanConfidenceCorrect,
            meanConfidenceIncorrect = r.MeanConfidenceIncorrect,
            perCategory = r.PerCategory.Select(c => new
            {
                name      = c.Name,
                tp        = c.TP,
                fp        = c.FP,
                fn        = c.FN,
                precision = c.Precision,
                recall    = c.Recall,
            }),
            misclassifications = r.Misclassifications.Select(m => new
            {
                description         = m.Description,
                expectedCategory    = m.ExpectedCategory,
                expectedSubCategory = m.ExpectedSubCategory,
                gotCategory         = m.GotCategory,
                gotSubCategory      = m.GotSubCategory,
                confidence          = m.Confidence,
            }),
        };

        var json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(path, json);
        Console.WriteLine($"Report written to {path}");
    }

    private static void PrintRow(string label, string value, string? pct = null) =>
        Console.WriteLine(pct is not null
            ? $"  {label,-22}: {value,-14} ({pct})"
            : $"  {label,-22}: {value}");

    private static string Pct(int num, int den) =>
        den == 0 ? "—" : $"{(double)num / den * 100:F1}%";

    private static string Trunc(string s, int max) =>
        s.Length <= max ? s : s[..(max - 1)] + "…";
}
