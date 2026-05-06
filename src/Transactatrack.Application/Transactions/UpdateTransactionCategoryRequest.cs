namespace Transactatrack.Application.Transactions;

public record UpdateTransactionCategoryRequest(Guid? CategoryId, Guid? SubCategoryId);
