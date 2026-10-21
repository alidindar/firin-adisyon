using System.Net;
using System.Net.Http.Json;
using Firin.Api.Domain;
using static Firin.Api.Endpoints.CatalogEndpoints;

namespace Firin.Tests;

public sealed class CatalogTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Owner_creates_category_and_products_and_catalog_lists_them()
    {
        var owner = await _app.OwnerAsync();
        var cat = await Seed.CategoryAsync(owner, "Kuru pasta");
        var gram = await Seed.ProductAsync(owner, cat.Id, "Tuzlu kurabiye", UnitType.Gram, 60000);
        await Seed.ProductAsync(owner, cat.Id, "Açma", UnitType.Piece, 2500);

        Assert.Equal(UnitType.Gram, gram.UnitType);
        Assert.Null(gram.PhotoUrl);

        var catalog = await (await owner.GetAsync("/api/catalog")).ReadAs<List<CatalogCategoryDto>>();
        var c = Assert.Single(catalog);
        Assert.Equal("Kuru pasta", c.Name);
        Assert.Equal(new[] { "Açma", "Tuzlu kurabiye" }, c.Products.Select(p => p.Name).Order().ToArray());
    }

    [Fact]
    public async Task Catalog_hides_inactive_products_and_empty_categories()
    {
        var owner = await _app.OwnerAsync();
        var cat = await Seed.CategoryAsync(owner, "İçecek");
        await Seed.CategoryAsync(owner, "Boş kategori");
        var tea = await Seed.ProductAsync(owner, cat.Id, "Çay", UnitType.Piece, 1500);

        var res = await owner.PutAsJsonAsync($"/api/admin/products/{tea.Id}",
            new { categoryId = cat.Id, name = "Çay", unitType = "Piece", priceKurus = 1500, isActive = false, sortOrder = 0 });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var catalog = await (await owner.GetAsync("/api/catalog")).ReadAs<List<CatalogCategoryDto>>();
        Assert.Empty(catalog);

        var all = await (await owner.GetAsync("/api/admin/products")).ReadAs<List<ProductDto>>();
        Assert.False(Assert.Single(all).IsActive);
    }

    [Fact]
    public async Task Staff_reads_catalog_but_cannot_manage()
    {
        var staff = await _app.StaffAsync();
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/catalog")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/products")).StatusCode);
        var res = await staff.PostAsJsonAsync("/api/admin/categories", new { name = "X", sortOrder = 0 });
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Anonymous_cannot_read_catalog()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.Client.GetAsync("/api/catalog")).StatusCode);
    }

    [Theory]
    [InlineData("", 1000, "ad_gerekli")]
    [InlineData("Simit", 0, "fiyat_gecersiz")]
    [InlineData("Simit", 10_000_001, "fiyat_gecersiz")]
    public async Task Invalid_product_is_rejected(string name, long price, string code)
    {
        var owner = await _app.OwnerAsync();
        var cat = await Seed.CategoryAsync(owner, "Ekmek");
        var res = await owner.PostAsJsonAsync("/api/admin/products",
            new { categoryId = cat.Id, name, unitType = "Piece", priceKurus = price, isActive = true, sortOrder = 0 });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal(code, await res.ProblemCode());
    }

    [Fact]
    public async Task Product_with_unknown_category_is_rejected()
    {
        var owner = await _app.OwnerAsync();
        var res = await owner.PostAsJsonAsync("/api/admin/products",
            new { categoryId = 999, name = "Simit", unitType = "Piece", priceKurus = 1500, isActive = true, sortOrder = 0 });
        Assert.Equal("kategori_yok", await res.ProblemCode());
    }

    [Fact]
    public async Task Category_with_products_cannot_be_deleted()
    {
        var owner = await _app.OwnerAsync();
        var full = await Seed.CategoryAsync(owner, "Dolu");
        var empty = await Seed.CategoryAsync(owner, "Boş");
        await Seed.ProductAsync(owner, full.Id, "Poğaça", UnitType.Piece, 2000);

        var res = await owner.DeleteAsync($"/api/admin/categories/{full.Id}");
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("kategori_dolu", await res.ProblemCode());

        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/api/admin/categories/{empty.Id}")).StatusCode);
        var cats = await (await owner.GetAsync("/api/admin/categories")).ReadAs<List<CategoryDto>>();
        Assert.Equal(1, Assert.Single(cats).ProductCount);
    }

    [Fact]
    public async Task Category_can_be_renamed()
    {
        var owner = await _app.OwnerAsync();
        var cat = await Seed.CategoryAsync(owner, "Tatlı");
        var res = await owner.PutAsJsonAsync($"/api/admin/categories/{cat.Id}", new { name = "Tatlılar", sortOrder = 2 });
        var dto = await res.ReadAs<CategoryDto>();
        Assert.Equal("Tatlılar", dto.Name);
        Assert.Equal(2, dto.SortOrder);
    }
}
