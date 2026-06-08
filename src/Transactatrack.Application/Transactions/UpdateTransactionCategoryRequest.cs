namespace Transactatrack.Application.Transactions;

// Note travels on this same request because both the inline category picker and the
// transaction detail modal save through PATCH /api/transactions/{id}. Callers must always
// send the transaction's current Note (the inline picker echoes it back) so a category-only
// edit does not wipe an existing note.
public record UpdateTransactionCategoryRequest(Guid? CategoryId, Guid? SubCategoryId, string? Note = null);
