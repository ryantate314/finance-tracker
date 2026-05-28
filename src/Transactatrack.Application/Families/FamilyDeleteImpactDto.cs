namespace Transactatrack.Application.Families;

public record FamilyDeleteImpactDto(
    Guid FamilyId,
    string FamilyName,
    int Owners,
    int Accounts,
    int Categories,
    int SubCategories,
    int CategoryRules,
    int ImportBatches,
    int Transactions
);
