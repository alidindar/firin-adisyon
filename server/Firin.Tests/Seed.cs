using System.Net.Http.Json;
using Firin.Api.Domain;
using static Firin.Api.Endpoints.CatalogEndpoints;

namespace Firin.Tests;

/// <summary>Testlerde veriyi gerçek API üzerinden oluşturur.</summary>
public static class Seed
{
    public static async Task<CategoryDto> CategoryAsync(HttpClient owner, string name, int sortOrder = 0)
    {
        var res = await owner.PostAsJsonAsync("/api/admin/categories", new { name, sortOrder });
        res.EnsureSuccessStatusCode();
        return await res.ReadAs<CategoryDto>();
    }

    public static async Task<ProductDto> ProductAsync(HttpClient owner, int categoryId, string name, UnitType unit, long priceKurus)
    {
        var res = await owner.PostAsJsonAsync("/api/admin/products",
            new { categoryId, name, unitType = unit.ToString(), priceKurus, isActive = true, sortOrder = 0 });
        res.EnsureSuccessStatusCode();
        return await res.ReadAs<ProductDto>();
    }
}
