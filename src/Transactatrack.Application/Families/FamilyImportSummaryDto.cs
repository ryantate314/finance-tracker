namespace Transactatrack.Application.Families;

public record FamilyImportSummaryDto(
    Guid FamilyId,
    string FamilyName,
    int OwnersInserted,
    int OwnersSkipped,
    int AccountsInserted,
    int AccountsSkipped,
    int CategoriesInserted,
    int CategoriesSkipped,
    int CategoriesRemapped,
    int SubCategoriesInserted,
    int SubCategoriesSkipped,
    int CategoryRulesInserted,
    int CategoryRulesSkipped,
    int ImportBatchesInserted,
    int ImportBatchesSkipped,
    int TransactionsInserted,
    int TransactionsSkipped
);
