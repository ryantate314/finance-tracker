using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using Transactatrack.Application.Accounts;
using Transactatrack.Application.Categories;
using Transactatrack.Application.Families;
using Transactatrack.Application.Imports;
using Transactatrack.Application.Owners;
using Transactatrack.Application.Transactions;
using Transactatrack.Domain.Enums;

namespace Transactatrack.IntegrationTests.Transfers;

/// <summary>HTTP helpers shared by the transfer + boundary-analytics integration tests.</summary>
public static class TransferTestHarness
{
    public record Row(string Date, string Description, decimal Amount);

    public record PagedResult<T>(List<T> Items, int TotalCount, int Page, int PageSize);

    public static async Task<HttpClient> NewFamilyClient(IntegrationTestFactory factory)
    {
        var root = factory.CreateClient();
        var resp = await root.PostAsJsonAsync("/api/families", new CreateFamilyRequest($"Family-{Guid.NewGuid():N}"));
        resp.EnsureSuccessStatusCode();
        var family = (await resp.Content.ReadFromJsonAsync<FamilyDto>(IntegrationTestFactory.JsonOpts))!;
        return factory.CreateClientWithFamily(family.Id);
    }

    public static async Task<Guid> CreateOwner(HttpClient client, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/owners", new CreateOwnerRequest(name));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<OwnerDto>(IntegrationTestFactory.JsonOpts))!.Id;
    }

    public static async Task<Guid> CreateAccount(HttpClient client, Guid ownerId, string name, AccountType accountType = AccountType.Checking)
    {
        var resp = await client.PostAsJsonAsync("/api/accounts",
            new CreateAccountRequest(ownerId, name, "Chase", accountType, "Chase"));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<AccountDto>(IntegrationTestFactory.JsonOpts))!.Id;
    }

    public static async Task<Guid> CreateCategory(HttpClient client, string name)
    {
        var resp = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest(name));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CategoryDto>(IntegrationTestFactory.JsonOpts))!.Id;
    }

    /// <summary>Id of a per-family system category (Transfer / Income).</summary>
    public static async Task<Guid> SystemCategoryId(HttpClient client, CategoryKind kind)
    {
        var cats = (await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories", IntegrationTestFactory.JsonOpts))!;
        return cats.Single(c => c.Kind == kind).Id;
    }

    /// <summary>Build a Chase-format CSV (canonical signs: outflows negative, inflows positive).</summary>
    public static string ChaseCsv(params Row[] rows)
    {
        var sb = new StringBuilder("Transaction Date,Post Date,Description,Amount\n");
        foreach (var r in rows)
            sb.Append($"{r.Date},{r.Date},{r.Description},{r.Amount.ToString(CultureInfo.InvariantCulture)}\n");
        return sb.ToString();
    }

    /// <summary>Upload a CSV to an account and commit it (which triggers transfer auto-matching).</summary>
    public static async Task ImportAndCommit(HttpClient client, Guid accountId, string csv)
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(accountId.ToString()), "accountId");
        var fileContent = new ByteArrayContent(Encoding.UTF8.GetBytes(csv));
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(fileContent, "file", "import.csv");

        var uploadResp = await client.PostAsync("/api/imports", form);
        uploadResp.EnsureSuccessStatusCode();
        var preview = (await uploadResp.Content.ReadFromJsonAsync<ImportPreviewDto>(IntegrationTestFactory.JsonOpts))!;
        (await client.PostAsync($"/api/imports/{preview.BatchId}/commit", null)).EnsureSuccessStatusCode();
    }

    public static async Task<List<TransactionDto>> Ledger(HttpClient client, params Guid[] accountIds)
    {
        var url = "/api/transactions?pageSize=200";
        if (accountIds.Length > 0) url += "&accountIds=" + string.Join(",", accountIds);
        var paged = (await client.GetFromJsonAsync<PagedResult<TransactionDto>>(url, IntegrationTestFactory.JsonOpts))!;
        return paged.Items;
    }

    public static async Task Categorize(HttpClient client, Guid txId, Guid categoryId)
    {
        var resp = await client.PatchAsJsonAsync($"/api/transactions/{txId}",
            new UpdateTransactionCategoryRequest(categoryId, null));
        resp.EnsureSuccessStatusCode();
    }
}
