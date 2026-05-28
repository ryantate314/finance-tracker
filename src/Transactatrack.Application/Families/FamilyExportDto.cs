using Transactatrack.Application.Accounts;
using Transactatrack.Application.Categories;
using Transactatrack.Application.CategoryRules;
using Transactatrack.Application.Imports;
using Transactatrack.Application.Owners;
using Transactatrack.Application.Transactions;

namespace Transactatrack.Application.Families;

public record FamilyExportDto(
    int ExportVersion,
    DateTime ExportedUtc,
    FamilyDto Family,
    IReadOnlyList<OwnerDto> Owners,
    IReadOnlyList<AccountDto> Accounts,
    IReadOnlyList<CategoryDto> Categories,
    IReadOnlyList<CategoryRuleDto> CategoryRules,
    IReadOnlyList<ImportBatchDto> ImportBatches,
    IReadOnlyList<TransactionDto> Transactions
);
