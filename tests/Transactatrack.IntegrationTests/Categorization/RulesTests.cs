using System.Net;
using System.Net.Http.Json;
using Transactatrack.Application.Accounts;
using Transactatrack.Application.CategoryRules;
using Transactatrack.Application.Categories;
using Transactatrack.Application.Families;
using Transactatrack.Application.Owners;
using Transactatrack.Domain.Enums;

namespace Transactatrack.IntegrationTests.Categorization;

public class RulesTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public RulesTests(IntegrationTestFactory factory) => _factory = factory;

    private async Task<(HttpClient client, Guid familyId, Guid categoryId, Guid accountId)> SetupAsync()
    {
        var rootClient = _factory.CreateClient();
        var familyResp = await rootClient.PostAsJsonAsync("/api/families", new CreateFamilyRequest($"Family-{Guid.NewGuid():N}"));
        var family = (await familyResp.Content.ReadFromJsonAsync<FamilyDto>(IntegrationTestFactory.JsonOpts))!;

        var client = _factory.CreateClientWithFamily(family.Id);

        var catResp = await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Shopping"));
        var category = (await catResp.Content.ReadFromJsonAsync<CategoryDto>(IntegrationTestFactory.JsonOpts))!;

        var ownerResp = await client.PostAsJsonAsync("/api/owners", new CreateOwnerRequest("Owner"));
        var owner = (await ownerResp.Content.ReadFromJsonAsync<OwnerDto>(IntegrationTestFactory.JsonOpts))!;
        var acctResp = await client.PostAsJsonAsync("/api/accounts",
            new CreateAccountRequest(owner.Id, "Chase Card", "Chase", AccountType.CreditCard, "Chase"));
        var account = (await acctResp.Content.ReadFromJsonAsync<AccountDto>(IntegrationTestFactory.JsonOpts))!;

        return (client, family.Id, category.Id, account.Id);
    }

    [Fact]
    public async Task Create_ValidContainsRule_Succeeds()
    {
        var (client, _, categoryId, _) = await SetupAsync();
        var request = new CreateCategoryRuleRequest(10, RuleMatchField.Description, RuleMatchType.Contains, "AMAZON",
            null, null, categoryId, RuleScope.Family, null, true);

        var resp = await client.PostAsJsonAsync("/api/category-rules", request);
        resp.EnsureSuccessStatusCode();

        var rule = (await resp.Content.ReadFromJsonAsync<CategoryRuleDto>(IntegrationTestFactory.JsonOpts))!;
        Assert.Equal("AMAZON", rule.Pattern);
        Assert.Equal(categoryId, rule.TargetCategoryId);
    }

    [Fact]
    public async Task Create_InvalidRegex_Returns400()
    {
        var (client, _, categoryId, _) = await SetupAsync();
        var request = new CreateCategoryRuleRequest(10, RuleMatchField.Description, RuleMatchType.Regex, "[unclosed",
            null, null, categoryId, RuleScope.Family, null, true);

        var resp = await client.PostAsJsonAsync("/api/category-rules", request);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_AmountRangeWithNoBounds_Returns400()
    {
        var (client, _, categoryId, _) = await SetupAsync();
        var request = new CreateCategoryRuleRequest(10, RuleMatchField.AmountRange, RuleMatchType.Contains, "",
            null, null, categoryId, RuleScope.Family, null, true);

        var resp = await client.PostAsJsonAsync("/api/category-rules", request);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Create_AccountScopeWithoutAccountId_Returns400()
    {
        var (client, _, categoryId, _) = await SetupAsync();
        var request = new CreateCategoryRuleRequest(10, RuleMatchField.Description, RuleMatchType.Contains, "AMAZON",
            null, null, categoryId, RuleScope.Account, null, true);

        var resp = await client.PostAsJsonAsync("/api/category-rules", request);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
    }

    [Fact]
    public async Task Reorder_UpdatesPriorities()
    {
        var (client, _, categoryId, _) = await SetupAsync();
        var r1 = await CreateRule(client, categoryId, "AMAZON", 10);
        var r2 = await CreateRule(client, categoryId, "COSTCO", 20);

        var updates = new[] { new RuleOrderUpdate(r1.Id, 30), new RuleOrderUpdate(r2.Id, 5) };
        var resp = await client.PutAsJsonAsync("/api/category-rules/order", updates);
        resp.EnsureSuccessStatusCode();

        var listResp = await client.GetFromJsonAsync<List<CategoryRuleDto>>(
            "/api/category-rules", IntegrationTestFactory.JsonOpts);
        var updated1 = listResp!.First(r => r.Id == r1.Id);
        var updated2 = listResp.First(r => r.Id == r2.Id);
        Assert.Equal(30, updated1.Priority);
        Assert.Equal(5, updated2.Priority);
    }

    [Fact]
    public async Task Rules_FamilyScoped_OtherFamilyCannotSee()
    {
        var (client, _, categoryId, _) = await SetupAsync();
        await CreateRule(client, categoryId, "AMAZON", 10);

        // Different family — should see no rules
        var rootClient = _factory.CreateClient();
        var f2Resp = await rootClient.PostAsJsonAsync("/api/families", new CreateFamilyRequest($"Family-{Guid.NewGuid():N}"));
        var f2 = (await f2Resp.Content.ReadFromJsonAsync<FamilyDto>(IntegrationTestFactory.JsonOpts))!;
        var client2 = _factory.CreateClientWithFamily(f2.Id);

        var rules = await client2.GetFromJsonAsync<List<CategoryRuleDto>>(
            "/api/category-rules", IntegrationTestFactory.JsonOpts);
        Assert.Empty(rules!);
    }

    private static async Task<CategoryRuleDto> CreateRule(HttpClient client, Guid categoryId, string pattern, int priority)
    {
        var request = new CreateCategoryRuleRequest(priority, RuleMatchField.Description, RuleMatchType.Contains, pattern,
            null, null, categoryId, RuleScope.Family, null, true);
        var resp = await client.PostAsJsonAsync("/api/category-rules", request);
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CategoryRuleDto>(IntegrationTestFactory.JsonOpts))!;
    }
}
