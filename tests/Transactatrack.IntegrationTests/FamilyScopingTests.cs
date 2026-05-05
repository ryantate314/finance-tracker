using System.Net;
using System.Net.Http.Json;
using Transactatrack.Application.Families;
using Transactatrack.Application.Owners;
using Transactatrack.Application.Accounts;
using Transactatrack.Application.Categories;
using Transactatrack.Domain.Enums;

namespace Transactatrack.IntegrationTests;

public class FamilyScopingTests : IClassFixture<IntegrationTestFactory>
{
    private readonly IntegrationTestFactory _factory;

    public FamilyScopingTests(IntegrationTestFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Families_PostThenGet_RoundTrips()
    {
        var client = _factory.CreateClient();
        var createResponse = await client.PostAsJsonAsync("/api/families", new CreateFamilyRequest("Test Family"));
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<FamilyDto>();
        Assert.NotNull(created);
        Assert.Equal("Test Family", created.Name);
        Assert.NotEqual(Guid.Empty, created.Id);

        var getResponse = await client.GetAsync($"/api/families/{created.Id}");
        getResponse.EnsureSuccessStatusCode();
        var fetched = await getResponse.Content.ReadFromJsonAsync<FamilyDto>();
        Assert.Equal(created.Id, fetched!.Id);
    }

    [Fact]
    public async Task Owners_AreScopedToActiveFamily()
    {
        // Create family A and add an owner
        var client = _factory.CreateClient();
        var familyAResp = await client.PostAsJsonAsync("/api/families", new CreateFamilyRequest("Family A"));
        var familyA = await familyAResp.Content.ReadFromJsonAsync<FamilyDto>();

        var familyBResp = await client.PostAsJsonAsync("/api/families", new CreateFamilyRequest("Family B"));
        var familyB = await familyBResp.Content.ReadFromJsonAsync<FamilyDto>();

        var clientA = _factory.CreateClientWithFamily(familyA!.Id);
        var ownerResp = await clientA.PostAsJsonAsync("/api/owners", new CreateOwnerRequest("Ryan"));
        ownerResp.EnsureSuccessStatusCode();

        // Family B should see no owners
        var clientB = _factory.CreateClientWithFamily(familyB!.Id);
        var listResp = await clientB.GetAsync("/api/owners");
        listResp.EnsureSuccessStatusCode();
        var owners = await listResp.Content.ReadFromJsonAsync<List<OwnerDto>>();
        Assert.Empty(owners!);
    }

    [Fact]
    public async Task Cannot_GetEntity_FromOtherFamily_Returns404()
    {
        var client = _factory.CreateClient();
        var familyAResp = await client.PostAsJsonAsync("/api/families", new CreateFamilyRequest("Family Cross-A"));
        var familyA = await familyAResp.Content.ReadFromJsonAsync<FamilyDto>();
        var familyBResp = await client.PostAsJsonAsync("/api/families", new CreateFamilyRequest("Family Cross-B"));
        var familyB = await familyBResp.Content.ReadFromJsonAsync<FamilyDto>();

        var clientA = _factory.CreateClientWithFamily(familyA!.Id);
        var ownerResp = await clientA.PostAsJsonAsync("/api/owners", new CreateOwnerRequest("Scoped Owner"));
        var owner = await ownerResp.Content.ReadFromJsonAsync<OwnerDto>();

        var clientB = _factory.CreateClientWithFamily(familyB!.Id);
        var getResp = await clientB.GetAsync($"/api/owners/{owner!.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
    }

    [Fact]
    public async Task DeleteFamily_WithDependentOwner_Returns409()
    {
        var client = _factory.CreateClient();
        var familyResp = await client.PostAsJsonAsync("/api/families", new CreateFamilyRequest("Family WithOwner"));
        var family = await familyResp.Content.ReadFromJsonAsync<FamilyDto>();

        var clientF = _factory.CreateClientWithFamily(family!.Id);
        await clientF.PostAsJsonAsync("/api/owners", new CreateOwnerRequest("Dependent Owner"));

        var deleteResp = await client.DeleteAsync($"/api/families/{family.Id}");
        Assert.Equal(HttpStatusCode.Conflict, deleteResp.StatusCode);
    }

    [Fact]
    public async Task Categories_CanCreateSubCategoryAndList()
    {
        var client = _factory.CreateClient();
        var familyResp = await client.PostAsJsonAsync("/api/families", new CreateFamilyRequest("Family Cats"));
        var family = await familyResp.Content.ReadFromJsonAsync<FamilyDto>();
        var clientF = _factory.CreateClientWithFamily(family!.Id);

        var categoryResp = await clientF.PostAsJsonAsync("/api/categories", new CreateCategoryRequest("Food"));
        categoryResp.EnsureSuccessStatusCode();
        var category = await categoryResp.Content.ReadFromJsonAsync<CategoryDto>();

        var subResp = await clientF.PostAsJsonAsync($"/api/categories/{category!.Id}/subcategories", new CreateSubCategoryRequest("Groceries"));
        subResp.EnsureSuccessStatusCode();
        var sub = await subResp.Content.ReadFromJsonAsync<SubCategoryDto>();

        var listResp = await clientF.GetAsync("/api/categories");
        var categories = await listResp.Content.ReadFromJsonAsync<List<CategoryDto>>();

        Assert.Single(categories!);
        var food = categories![0];
        Assert.Equal("Food", food.Name);
        Assert.Single(food.SubCategories);
        Assert.Equal("Groceries", food.SubCategories[0].Name);
        Assert.Equal(category.Id, food.SubCategories[0].CategoryId);
    }

    [Fact]
    public async Task Accounts_RejectInvalidOwnerId_Returns400()
    {
        var client = _factory.CreateClient();
        var familyResp = await client.PostAsJsonAsync("/api/families", new CreateFamilyRequest("Family Accounts"));
        var family = await familyResp.Content.ReadFromJsonAsync<FamilyDto>();
        var clientF = _factory.CreateClientWithFamily(family!.Id);

        var createResp = await clientF.PostAsJsonAsync("/api/accounts", new CreateAccountRequest(
            Guid.NewGuid(), "My Account", null, AccountType.Checking, null));

        Assert.Equal(HttpStatusCode.BadRequest, createResp.StatusCode);
    }
}
