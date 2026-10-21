using Firin.Api.Auth;
using Firin.Api.Data;
using Firin.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Firin.Api.Endpoints;

public static class CatalogEndpoints
{
    public record ProductDto(int Id, int CategoryId, string Name, UnitType UnitType, long PriceKurus,
        string? PhotoUrl, bool IsActive, int SortOrder);
    public record CategoryDto(int Id, string Name, int SortOrder, int ProductCount);
    public record CatalogCategoryDto(int Id, string Name, List<ProductDto> Products);
    public record CategoryInput(string Name, int SortOrder);
    public record ProductInput(int CategoryId, string Name, UnitType? UnitType, long PriceKurus, bool? IsActive, int SortOrder);

    public static ProductDto ToDto(this Product p) => new(p.Id, p.CategoryId, p.Name, p.UnitType, p.PriceKurus,
        p.PhotoFile is null ? null : $"/photos/{p.PhotoFile}", p.IsActive, p.SortOrder);

    public static void MapCatalogEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/catalog", async (AppDb db) =>
        {
            var cats = await db.Categories.AsNoTracking()
                .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
                .Select(c => new
                {
                    c.Id,
                    c.Name,
                    Products = c.Products.Where(p => p.IsActive).OrderBy(p => p.SortOrder).ThenBy(p => p.Name).ToList(),
                })
                .ToListAsync();
            return cats.Where(c => c.Products.Count > 0)
                .Select(c => new CatalogCategoryDto(c.Id, c.Name, c.Products.Select(p => p.ToDto()).ToList()))
                .ToList();
        }).RequireAuthorization();

        var cats = api.MapGroup("/admin/categories").RequireAuthorization(AuthSetup.OwnerPolicy);

        cats.MapGet("/", async (AppDb db) => await db.Categories
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.SortOrder, c.Products.Count))
            .ToListAsync());

        cats.MapPost("/", async (CategoryInput input, AppDb db) =>
        {
            var c = new Category { Name = Validate.Name(input.Name), SortOrder = input.SortOrder };
            db.Categories.Add(c);
            await db.SaveChangesAsync();
            return new CategoryDto(c.Id, c.Name, c.SortOrder, 0);
        });

        cats.MapPut("/{id:int}", async (int id, CategoryInput input, AppDb db) =>
        {
            var c = await db.Categories.FindAsync(id) ?? throw Validate.NotFound();
            c.Name = Validate.Name(input.Name);
            c.SortOrder = input.SortOrder;
            await db.SaveChangesAsync();
            return new CategoryDto(c.Id, c.Name, c.SortOrder, await db.Products.CountAsync(p => p.CategoryId == id));
        });

        cats.MapDelete("/{id:int}", async (int id, AppDb db) =>
        {
            var c = await db.Categories.FindAsync(id) ?? throw Validate.NotFound();
            if (await db.Products.AnyAsync(p => p.CategoryId == id))
                throw new DomainException("kategori_dolu",
                    "Bu kategoride ürün var. Önce ürünleri başka kategoriye taşıyın.", StatusCodes.Status409Conflict);
            db.Categories.Remove(c);
            await db.SaveChangesAsync();
            return Results.NoContent();
        });

        var products = api.MapGroup("/admin/products").RequireAuthorization(AuthSetup.OwnerPolicy);

        products.MapGet("/", async (AppDb db) =>
            (await db.Products.AsNoTracking().OrderBy(p => p.SortOrder).ThenBy(p => p.Name).ToListAsync())
                .Select(p => p.ToDto()).ToList());

        products.MapPost("/", async (ProductInput input, AppDb db) =>
        {
            var p = new Product { Name = "" };
            await ApplyAsync(p, input, db);
            db.Products.Add(p);
            await db.SaveChangesAsync();
            return p.ToDto();
        });

        products.MapPut("/{id:int}", async (int id, ProductInput input, AppDb db) =>
        {
            var p = await db.Products.FindAsync(id) ?? throw Validate.NotFound();
            await ApplyAsync(p, input, db);
            await db.SaveChangesAsync();
            return p.ToDto();
        });
    }

    private static async Task ApplyAsync(Product p, ProductInput input, AppDb db)
    {
        if (!await db.Categories.AnyAsync(c => c.Id == input.CategoryId))
            throw new DomainException("kategori_yok", "Kategori bulunamadı.");
        if (input.UnitType is not { } unitType || !Enum.IsDefined(unitType))
            throw new DomainException("birim_gecersiz", "Satış birimi geçersiz.");
        var isActive = input.IsActive ?? throw new DomainException("aktiflik_gecersiz", "Aktiflik durumu belirtilmeli.");
        p.CategoryId = input.CategoryId;
        p.Name = Validate.Name(input.Name);
        p.UnitType = unitType;
        p.PriceKurus = Validate.Price(input.PriceKurus);
        p.IsActive = isActive;
        p.SortOrder = input.SortOrder;
    }
}
