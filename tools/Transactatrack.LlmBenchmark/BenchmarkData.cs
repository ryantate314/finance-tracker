namespace Transactatrack.LlmBenchmark;

public record BenchmarkInput(
    List<BenchmarkCategory> Categories,
    List<LabeledTransaction> Transactions);

public record BenchmarkCategory(
    string Name,
    List<string> SubCategories);

public record LabeledTransaction(
    string Date,
    decimal Amount,
    string Description,
    string? Merchant,
    string ExpectedCategory,
    string? ExpectedSubCategory);
