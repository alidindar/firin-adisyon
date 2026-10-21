# Fırın Adisyon — Parça 2: Katalog Yönetimi — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fırın sahibi kategorileri, ürünleri (adet/gram, fiyat, fotoğraf), masaları ve kullanıcıları yönetebilsin. Garsonlar aktif ürün kataloğunu okuyabilsin.

**Architecture:** Yönetim uçları `/api/admin/*` altında, `Owner` policy'siyle korunur. Okuma ucu `/api/catalog` her oturum açmış kullanıcıya açıktır. Fotoğraflar tarayıcıda 800 px JPEG'e küçültülüp `multipart/form-data` ile yüklenir, `data/photos/` içine yazılır ve `/photos/<dosya>` adresinden sunulur. Arayüzde `/yonetim` altında sekmeli bir sayfa olur.

**Tech Stack:** Parça 1 ile aynı. Yeni paket yok.

**Ön koşul:** Parça 1 tamamlanmış olmalı (`docs/superpowers/plans/2026-10-21-1-iskelet-ve-giris.md`).

## Global Constraints

- Hedef `net10.0`, `<Nullable>enable</Nullable>`; Node 24, TypeScript `strict`.
- Para her yerde **kuruş** cinsinden tam sayıdır: C# `long`, TS `number`. Float/decimal saklanmaz.
- Gramlı ürünün `PriceKurus` değeri **kilogram** fiyatıdır; adetli üründe adet fiyatıdır.
- Zaman her zaman DI'daki `TimeProvider` üzerinden alınır.
- API JSON camelCase, enum'lar string. Hatalar `DomainException(code, message, status)` ile atılır; `title` Türkçe, `code` makine kodu.
- Arayüz metinleri Türkçe ve doğru diakritikli. Dokunma hedefleri en az 48 px. 394 px genişlikte yatay kaydırma yok.
- Ürün silinmez, pasife alınır (geçmiş adisyonlar ürüne bağlıdır).
- Commit adımları: kullanıcı yürütme başında toplu onay vermediyse commit atmadan önce sor. Commit mesajı sonuna `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` eklenir.

## Parça 1'den kullanılanlar

- `DomainException(string code, string message, int status = 400)` — `server/Firin.Api/DomainException.cs`
- `AppDb` ve entity'ler (`Category`, `Product`, `DiningTable`, `User`, `Role`, `UnitType`) — `server/Firin.Api/Data/AppDb.cs`, `server/Firin.Api/Domain/Entities.cs`
- `AuthSetup.OwnerPolicy`, `PinHasher`, `AuthEndpoints.PinFormatError()` — `server/Firin.Api/Auth/*`, `server/Firin.Api/Endpoints/AuthEndpoints.cs`
- `FirinPaths.PhotosDir` — `server/Firin.Api/FirinPaths.cs`
- Test: `TestApp` (`OwnerAsync()`, `StaffAsync()`, `LoginAsync()`, `SeedUser()`, `Db()`, `Json`), `HttpExt.ReadAs<T>()`, `HttpExt.ProblemCode()` — `server/Firin.Tests/TestApp.cs`
- Web: `api`, `ApiError` (`web/src/api/client.ts`), `formatKurus`, `parseTlToKurus` (`web/src/lib/format.ts`), `Shell` (`web/src/components/Shell.tsx`), CSS sınıfları `.page`, `.form`, `.error`, `button.primary`, `button.danger`

## Dosya haritası (bu parça)

| Dosya | Sorumluluk |
|---|---|
| `server/Firin.Api/Endpoints/Validate.cs` | Ad, fiyat doğrulama ve `NotFound()` |
| `server/Firin.Api/Endpoints/CatalogEndpoints.cs` | `/api/catalog`, `/api/admin/categories`, `/api/admin/products` |
| `server/Firin.Api/Endpoints/PhotoEndpoints.cs` | `/api/admin/products/{id}/photo` |
| `server/Firin.Api/Endpoints/TableAdminEndpoints.cs` | `/api/admin/tables` |
| `server/Firin.Api/Endpoints/UserAdminEndpoints.cs` | `/api/admin/users` |
| `server/Firin.Tests/Seed.cs` | Testlerde API üzerinden kategori/ürün/masa oluşturma |
| `web/src/api/admin.ts` | Yönetim API tipleri ve çağrıları |
| `web/src/lib/image.ts` | Fotoğrafı tarayıcıda küçültme |
| `web/src/components/Modal.tsx` | Basit diyalog |
| `web/src/pages/admin/*.tsx` | Yönetim sayfası ve sekmeleri |

---

### Task 1: Kategori ve ürün API'si

**Files:**
- Create: `server/Firin.Api/Endpoints/Validate.cs`, `server/Firin.Api/Endpoints/CatalogEndpoints.cs`
- Modify: `server/Firin.Api/Program.cs`
- Test: `server/Firin.Tests/Seed.cs`, `server/Firin.Tests/CatalogTests.cs`

**Interfaces:**
- Produces:
  - `Validate.Name(string? value, int max = 60) → string`, `Validate.Price(long kurus) → long`, `Validate.NotFound() → DomainException`
  - `CatalogEndpoints.ProductDto(int Id, int CategoryId, string Name, UnitType UnitType, long PriceKurus, string? PhotoUrl, bool IsActive, int SortOrder)`, `CategoryDto(int Id, string Name, int SortOrder, int ProductCount)`, `CatalogCategoryDto(int Id, string Name, List<ProductDto> Products)`, `CategoryInput(string Name, int SortOrder)`, `ProductInput(int CategoryId, string Name, UnitType UnitType, long PriceKurus, bool IsActive, int SortOrder)`, uzantı `Product.ToDto()`
  - `GET /api/catalog → List<CatalogCategoryDto>` (yalnızca aktif ürünler, ürünü olmayan kategori gizli)
  - `GET|POST /api/admin/categories`, `PUT|DELETE /api/admin/categories/{id}`; `GET|POST /api/admin/products`, `PUT /api/admin/products/{id}`
  - Test: `Seed.CategoryAsync(HttpClient owner, string name, int sortOrder = 0) → CategoryDto`, `Seed.ProductAsync(HttpClient owner, int categoryId, string name, UnitType unit, long priceKurus) → ProductDto`

- [ ] **Step 1: Test yardımcısını yaz**

`server/Firin.Tests/Seed.cs`:

```csharp
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
```

- [ ] **Step 2: Başarısız testleri yaz**

`server/Firin.Tests/CatalogTests.cs`:

```csharp
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
```

- [ ] **Step 3: Testleri çalıştır, başarısız olduğunu gör**

Run: `dotnet test server/Firin.Tests --filter CatalogTests`
Expected: FAIL. `CatalogEndpoints` bulunamadığı için derleme hatası.

- [ ] **Step 4: Doğrulama yardımcısını yaz**

`server/Firin.Api/Endpoints/Validate.cs`:

```csharp
namespace Firin.Api.Endpoints;

public static class Validate
{
    /// <summary>100.000 ₺</summary>
    public const long MaxPriceKurus = 10_000_000;

    public static string Name(string? value, int max = 60)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) throw new DomainException("ad_gerekli", "Ad boş olamaz.");
        if (v.Length > max) throw new DomainException("ad_uzun", $"Ad en fazla {max} karakter olabilir.");
        return v;
    }

    public static long Price(long kurus) => kurus is > 0 and <= MaxPriceKurus
        ? kurus
        : throw new DomainException("fiyat_gecersiz", "Fiyat 0'dan büyük, en fazla 100.000 ₺ olmalı.");

    public static DomainException NotFound() =>
        new("kayit_yok", "Kayıt bulunamadı.", StatusCodes.Status404NotFound);
}
```

- [ ] **Step 5: Katalog uçlarını yaz**

`server/Firin.Api/Endpoints/CatalogEndpoints.cs`:

```csharp
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
    public record ProductInput(int CategoryId, string Name, UnitType UnitType, long PriceKurus, bool IsActive, int SortOrder);

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
        if (!Enum.IsDefined(input.UnitType))
            throw new DomainException("birim_gecersiz", "Satış birimi geçersiz.");
        p.CategoryId = input.CategoryId;
        p.Name = Validate.Name(input.Name);
        p.UnitType = input.UnitType;
        p.PriceKurus = Validate.Price(input.PriceKurus);
        p.IsActive = input.IsActive;
        p.SortOrder = input.SortOrder;
    }
}
```

- [ ] **Step 6: Program.cs'e bağla**

`api.MapAuthEndpoints();` satırının altına:

```csharp
api.MapCatalogEndpoints();
```

- [ ] **Step 7: Testleri çalıştır, geçtiğini gör**

Run: `dotnet test server/Firin.Tests`
Expected: PASS (tüm testler; CatalogTests 10 test)

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: kategori ve ürün yönetim API'si"
```

### Task 2: Ürün fotoğrafı yükleme

**Files:**
- Create: `server/Firin.Api/Endpoints/PhotoEndpoints.cs`
- Modify: `server/Firin.Api/Program.cs`
- Test: `server/Firin.Tests/PhotoTests.cs`

**Interfaces:**
- Consumes: `Product.ToDto()`, `Validate.NotFound()` (Task 1); `FirinPaths.PhotosDir` (Parça 1)
- Produces: `POST /api/admin/products/{id}/photo` (multipart, alan adı `file`; JPEG/PNG/WebP, en fazla 2 MB) `→ ProductDto`; `DELETE /api/admin/products/{id}/photo → ProductDto`; `GET /photos/{dosya}` uzun süreli önbellekle sunulur. Dosya adı her yüklemede yenidir (`{productId}-{guid}.jpg`), eski dosya silinir.

- [ ] **Step 1: Başarısız testleri yaz**

`server/Firin.Tests/PhotoTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Headers;
using Firin.Api.Domain;
using static Firin.Api.Endpoints.CatalogEndpoints;

namespace Firin.Tests;

public sealed class PhotoTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    private static readonly byte[] FakeJpeg = [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3, 4];

    private static MultipartFormDataContent Upload(byte[] bytes, string contentType = "image/jpeg")
    {
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { file, "file", "foto.jpg" } };
    }

    private async Task<(HttpClient owner, ProductDto product)> ProductAsync()
    {
        var owner = await _app.OwnerAsync();
        var cat = await Seed.CategoryAsync(owner, "Pasta");
        return (owner, await Seed.ProductAsync(owner, cat.Id, "Yaş pasta", UnitType.Piece, 9000));
    }

    private string PhotoPath(string url) => Path.Combine(_app.DataDir, "photos", url["/photos/".Length..]);

    [Fact]
    public async Task Upload_saves_file_and_serves_it()
    {
        var (owner, p) = await ProductAsync();
        var res = await owner.PostAsync($"/api/admin/products/{p.Id}/photo", Upload(FakeJpeg));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var dto = await res.ReadAs<ProductDto>();
        Assert.StartsWith("/photos/", dto.PhotoUrl);
        Assert.EndsWith(".jpg", dto.PhotoUrl);
        Assert.True(File.Exists(PhotoPath(dto.PhotoUrl!)));

        var served = await _app.Client.GetAsync(dto.PhotoUrl);
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal(FakeJpeg, await served.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Replacing_photo_deletes_old_file()
    {
        var (owner, p) = await ProductAsync();
        var first = await (await owner.PostAsync($"/api/admin/products/{p.Id}/photo", Upload(FakeJpeg))).ReadAs<ProductDto>();
        var second = await (await owner.PostAsync($"/api/admin/products/{p.Id}/photo", Upload(FakeJpeg, "image/png"))).ReadAs<ProductDto>();

        Assert.NotEqual(first.PhotoUrl, second.PhotoUrl);
        Assert.EndsWith(".png", second.PhotoUrl);
        Assert.False(File.Exists(PhotoPath(first.PhotoUrl!)));
        Assert.True(File.Exists(PhotoPath(second.PhotoUrl!)));
    }

    [Fact]
    public async Task Delete_photo_removes_file()
    {
        var (owner, p) = await ProductAsync();
        var up = await (await owner.PostAsync($"/api/admin/products/{p.Id}/photo", Upload(FakeJpeg))).ReadAs<ProductDto>();
        var del = await (await owner.DeleteAsync($"/api/admin/products/{p.Id}/photo")).ReadAs<ProductDto>();
        Assert.Null(del.PhotoUrl);
        Assert.False(File.Exists(PhotoPath(up.PhotoUrl!)));
    }

    [Fact]
    public async Task Wrong_type_is_rejected()
    {
        var (owner, p) = await ProductAsync();
        var res = await owner.PostAsync($"/api/admin/products/{p.Id}/photo", Upload(FakeJpeg, "text/plain"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("foto_tur", await res.ProblemCode());
    }

    [Fact]
    public async Task Too_big_is_rejected()
    {
        var (owner, p) = await ProductAsync();
        var res = await owner.PostAsync($"/api/admin/products/{p.Id}/photo", Upload(new byte[2 * 1024 * 1024 + 1]));
        Assert.Equal("foto_boyut", await res.ProblemCode());
    }

    [Fact]
    public async Task Staff_cannot_upload()
    {
        var (_, p) = await ProductAsync();
        var staff = await _app.StaffAsync();
        var res = await staff.PostAsync($"/api/admin/products/{p.Id}/photo", Upload(FakeJpeg));
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}
```

- [ ] **Step 2: Testleri çalıştır, başarısız olduğunu gör**

Run: `dotnet test server/Firin.Tests --filter PhotoTests`
Expected: FAIL. Yükleme ucu yok, SPA fallback'e düşmeyen `/api` yolu 404 döner.

- [ ] **Step 3: Fotoğraf uçlarını yaz**

`server/Firin.Api/Endpoints/PhotoEndpoints.cs`:

```csharp
using Firin.Api.Auth;
using Firin.Api.Data;

namespace Firin.Api.Endpoints;

public static class PhotoEndpoints
{
    public const long MaxBytes = 2 * 1024 * 1024;

    private static readonly Dictionary<string, string> Extensions = new()
    {
        ["image/jpeg"] = ".jpg",
        ["image/png"] = ".png",
        ["image/webp"] = ".webp",
    };

    public static void MapPhotoEndpoints(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/admin/products/{id:int}/photo").RequireAuthorization(AuthSetup.OwnerPolicy);

        // Cookie SameSite=Strict olduğu için antiforgery token'a gerek yok.
        g.MapPost("/", async (int id, IFormFile file, AppDb db, FirinPaths paths) =>
        {
            var p = await db.Products.FindAsync(id) ?? throw Validate.NotFound();
            if (!Extensions.TryGetValue(file.ContentType, out var ext))
                throw new DomainException("foto_tur", "Fotoğraf JPEG, PNG ya da WebP olmalı.");
            if (file.Length is 0 or > MaxBytes)
                throw new DomainException("foto_boyut", "Fotoğraf en fazla 2 MB olabilir.");

            var name = $"{p.Id}-{Guid.NewGuid():N}{ext}";
            await using (var fs = File.Create(Path.Combine(paths.PhotosDir, name)))
                await file.CopyToAsync(fs);

            var old = p.PhotoFile;
            p.PhotoFile = name;
            await db.SaveChangesAsync();
            DeleteFile(paths, old);
            return p.ToDto();
        }).DisableAntiforgery();

        g.MapDelete("/", async (int id, AppDb db, FirinPaths paths) =>
        {
            var p = await db.Products.FindAsync(id) ?? throw Validate.NotFound();
            var old = p.PhotoFile;
            p.PhotoFile = null;
            await db.SaveChangesAsync();
            DeleteFile(paths, old);
            return p.ToDto();
        });
    }

    private static void DeleteFile(FirinPaths paths, string? name)
    {
        if (name is null) return;
        try { File.Delete(Path.Combine(paths.PhotosDir, name)); } catch (IOException) { }
    }
}
```

- [ ] **Step 4: Program.cs'e bağla ve fotoğrafları sun**

Başa `using Microsoft.Extensions.FileProviders;` ekle.

`app.Services.GetRequiredService<FirinPaths>(); // klasörleri hemen oluştur` satırını şununla değiştir:

```csharp
var paths = app.Services.GetRequiredService<FirinPaths>(); // klasörleri hemen oluştur
```

`app.UseStaticFiles();` satırının altına:

```csharp
// Dosya adı her yüklemede değiştiği için kalıcı önbellek güvenli.
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(paths.PhotosDir),
    RequestPath = "/photos",
    OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable",
});
```

`api.MapCatalogEndpoints();` satırının altına:

```csharp
api.MapPhotoEndpoints();
```

- [ ] **Step 5: Testleri çalıştır, geçtiğini gör**

Run: `dotnet test server/Firin.Tests`
Expected: PASS (tüm testler; PhotoTests 6 test)

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: ürün fotoğrafı yükleme ve sunma"
```

### Task 3: Masa ve kullanıcı yönetim API'si

**Files:**
- Create: `server/Firin.Api/Endpoints/TableAdminEndpoints.cs`, `server/Firin.Api/Endpoints/UserAdminEndpoints.cs`
- Modify: `server/Firin.Api/Program.cs`, `server/Firin.Tests/Seed.cs`
- Test: `server/Firin.Tests/TableAdminTests.cs`, `server/Firin.Tests/UserAdminTests.cs`

**Interfaces:**
- Consumes: `Validate` (Task 1); `PinHasher`, `PinLockout`, `AuthEndpoints.PinFormatError()` (Parça 1)
- Produces:
  - `TableAdminEndpoints.TableDto(int Id, string Name, int SortOrder, bool IsActive)`, `TableInput(string Name, int SortOrder, bool IsActive)`; `GET|POST /api/admin/tables`, `PUT /api/admin/tables/{id}`. Hata kodları: `masa_adi_var` (409), `masa_acik` (409)
  - `UserAdminEndpoints.UserDto(int Id, string Name, Role Role, bool IsActive)`, `UserCreateInput(string Name, string Pin, Role Role)`, `UserUpdateInput(string Name, Role Role, bool IsActive, string? Pin)`; `GET|POST /api/admin/users`, `PUT /api/admin/users/{id}`. Hata kodları: `kullanici_adi_var` (409), `son_sahip` (409)
  - Test: `Seed.TableAsync(HttpClient owner, string name, int sortOrder = 0) → TableDto`

- [ ] **Step 1: Seed yardımcısını genişlet**

`server/Firin.Tests/Seed.cs` başına `using static Firin.Api.Endpoints.TableAdminEndpoints;` ekle ve sınıfa şunu ekle:

```csharp
    public static async Task<TableDto> TableAsync(HttpClient owner, string name, int sortOrder = 0)
    {
        var res = await owner.PostAsJsonAsync("/api/admin/tables", new { name, sortOrder, isActive = true });
        res.EnsureSuccessStatusCode();
        return await res.ReadAs<TableDto>();
    }
```

- [ ] **Step 2: Başarısız testleri yaz**

`server/Firin.Tests/TableAdminTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Firin.Api.Domain;
using static Firin.Api.Endpoints.TableAdminEndpoints;

namespace Firin.Tests;

public sealed class TableAdminTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Owner_creates_and_lists_tables_in_order()
    {
        var owner = await _app.OwnerAsync();
        await Seed.TableAsync(owner, "Masa 2", sortOrder: 2);
        await Seed.TableAsync(owner, "Masa 1", sortOrder: 1);
        await Seed.TableAsync(owner, "Tezgâh", sortOrder: 0);

        var list = await (await owner.GetAsync("/api/admin/tables")).ReadAs<List<TableDto>>();
        Assert.Equal(new[] { "Tezgâh", "Masa 1", "Masa 2" }, list.Select(t => t.Name).ToArray());
    }

    [Fact]
    public async Task Duplicate_table_name_is_rejected()
    {
        var owner = await _app.OwnerAsync();
        await Seed.TableAsync(owner, "Masa 1");
        var res = await owner.PostAsJsonAsync("/api/admin/tables", new { name = " Masa 1 ", sortOrder = 0, isActive = true });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("masa_adi_var", await res.ProblemCode());
    }

    [Fact]
    public async Task Table_with_open_order_cannot_be_deactivated()
    {
        var owner = await _app.OwnerAsync();
        var t = await Seed.TableAsync(owner, "Masa 1");
        _app.Db(db =>
        {
            db.Orders.Add(new Order { TableId = t.Id, OpenedById = db.Users.First().Id, OpenedAt = _app.Clock.GetUtcNow().UtcDateTime });
            return db.SaveChanges();
        });

        var res = await owner.PutAsJsonAsync($"/api/admin/tables/{t.Id}", new { name = "Masa 1", sortOrder = 0, isActive = false });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("masa_acik", await res.ProblemCode());

        var rename = await owner.PutAsJsonAsync($"/api/admin/tables/{t.Id}", new { name = "Bahçe 1", sortOrder = 0, isActive = true });
        Assert.Equal("Bahçe 1", (await rename.ReadAs<TableDto>()).Name);
    }

    [Fact]
    public async Task Staff_cannot_manage_tables()
    {
        var staff = await _app.StaffAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/tables")).StatusCode);
    }
}
```

`server/Firin.Tests/UserAdminTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Firin.Api.Domain;
using static Firin.Api.Endpoints.AuthEndpoints;
using static Firin.Api.Endpoints.UserAdminEndpoints;

namespace Firin.Tests;

public sealed class UserAdminTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    private static async Task<int> MyIdAsync(HttpClient c) => (await (await c.GetAsync("/api/auth/me")).ReadAs<MeDto>()).Id;

    [Fact]
    public async Task Owner_creates_staff_who_can_log_in()
    {
        var owner = await _app.OwnerAsync();
        var res = await owner.PostAsJsonAsync("/api/admin/users", new { name = "Mehmet", pin = "4321", role = "Staff" });
        var u = await res.ReadAs<UserDto>();
        Assert.Equal(Role.Staff, u.Role);
        Assert.True(u.IsActive);

        var staff = await _app.LoginAsync(u.Id, "4321");
        Assert.Equal(Role.Staff, (await (await staff.GetAsync("/api/auth/me")).ReadAs<MeDto>()).Role);
    }

    [Fact]
    public async Task Duplicate_user_name_is_rejected()
    {
        var owner = await _app.OwnerAsync();
        await owner.PostAsJsonAsync("/api/admin/users", new { name = "Mehmet", pin = "4321", role = "Staff" });
        var res = await owner.PostAsJsonAsync("/api/admin/users", new { name = "Mehmet", pin = "1111", role = "Staff" });
        Assert.Equal("kullanici_adi_var", await res.ProblemCode());
    }

    [Fact]
    public async Task Bad_pin_is_rejected()
    {
        var owner = await _app.OwnerAsync();
        var res = await owner.PostAsJsonAsync("/api/admin/users", new { name = "Mehmet", pin = "12", role = "Staff" });
        Assert.Equal("pin_gecersiz", await res.ProblemCode());
    }

    [Fact]
    public async Task Last_owner_cannot_be_demoted_or_deactivated()
    {
        var owner = await _app.OwnerAsync();
        var id = await MyIdAsync(owner);

        var demote = await owner.PutAsJsonAsync($"/api/admin/users/{id}", new { name = "Sahip", role = "Staff", isActive = true, pin = (string?)null });
        Assert.Equal(HttpStatusCode.Conflict, demote.StatusCode);
        Assert.Equal("son_sahip", await demote.ProblemCode());

        var deactivate = await owner.PutAsJsonAsync($"/api/admin/users/{id}", new { name = "Sahip", role = "Owner", isActive = false, pin = (string?)null });
        Assert.Equal("son_sahip", await deactivate.ProblemCode());

        await owner.PostAsJsonAsync("/api/admin/users", new { name = "Ortak", pin = "2222", role = "Owner" });
        var ok = await owner.PutAsJsonAsync($"/api/admin/users/{id}", new { name = "Sahip", role = "Staff", isActive = true, pin = (string?)null });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Pin_can_be_changed()
    {
        var owner = await _app.OwnerAsync();
        var u = await (await owner.PostAsJsonAsync("/api/admin/users", new { name = "Mehmet", pin = "4321", role = "Staff" })).ReadAs<UserDto>();

        await owner.PutAsJsonAsync($"/api/admin/users/{u.Id}", new { name = "Mehmet", role = "Staff", isActive = true, pin = "9999" });

        var old = await _app.NewClient().PostAsJsonAsync("/api/auth/login", new { userId = u.Id, pin = "4321" });
        Assert.Equal(HttpStatusCode.Unauthorized, old.StatusCode);
        await _app.LoginAsync(u.Id, "9999");
    }

    [Fact]
    public async Task Staff_cannot_manage_users()
    {
        var staff = await _app.StaffAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/users")).StatusCode);
    }
}
```

- [ ] **Step 3: Testleri çalıştır, başarısız olduğunu gör**

Run: `dotnet test server/Firin.Tests --filter "TableAdminTests|UserAdminTests"`
Expected: FAIL. `TableAdminEndpoints` / `UserAdminEndpoints` bulunamadığı için derleme hatası.

- [ ] **Step 4: Masa yönetim uçlarını yaz**

`server/Firin.Api/Endpoints/TableAdminEndpoints.cs`:

```csharp
using Firin.Api.Auth;
using Firin.Api.Data;
using Firin.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Firin.Api.Endpoints;

public static class TableAdminEndpoints
{
    public record TableDto(int Id, string Name, int SortOrder, bool IsActive);
    public record TableInput(string Name, int SortOrder, bool IsActive);

    private static TableDto ToDto(DiningTable t) => new(t.Id, t.Name, t.SortOrder, t.IsActive);

    public static void MapTableAdminEndpoints(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/admin/tables").RequireAuthorization(AuthSetup.OwnerPolicy);

        g.MapGet("/", async (AppDb db) =>
            (await db.Tables.AsNoTracking().OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync())
                .Select(ToDto).ToList());

        g.MapPost("/", async (TableInput input, AppDb db) =>
        {
            var t = new DiningTable { Name = await UniqueNameAsync(db, input.Name, null), SortOrder = input.SortOrder, IsActive = input.IsActive };
            db.Tables.Add(t);
            await db.SaveChangesAsync();
            return ToDto(t);
        });

        g.MapPut("/{id:int}", async (int id, TableInput input, AppDb db) =>
        {
            var t = await db.Tables.FindAsync(id) ?? throw Validate.NotFound();
            t.Name = await UniqueNameAsync(db, input.Name, id);
            t.SortOrder = input.SortOrder;
            if (t.IsActive && !input.IsActive &&
                await db.Orders.AnyAsync(o => o.TableId == id && o.Status == OrderStatus.Open))
                throw new DomainException("masa_acik", "Bu masada açık hesap var. Önce hesabı kapatın.", StatusCodes.Status409Conflict);
            t.IsActive = input.IsActive;
            await db.SaveChangesAsync();
            return ToDto(t);
        });
    }

    private static async Task<string> UniqueNameAsync(AppDb db, string? raw, int? selfId)
    {
        var name = Validate.Name(raw, 30);
        if (await db.Tables.AnyAsync(t => t.Name == name && t.Id != selfId))
            throw new DomainException("masa_adi_var", "Bu adda bir masa zaten var.", StatusCodes.Status409Conflict);
        return name;
    }
}
```

- [ ] **Step 5: Kullanıcı yönetim uçlarını yaz**

`server/Firin.Api/Endpoints/UserAdminEndpoints.cs`:

```csharp
using Firin.Api.Auth;
using Firin.Api.Data;
using Firin.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Firin.Api.Endpoints;

public static class UserAdminEndpoints
{
    public record UserDto(int Id, string Name, Role Role, bool IsActive);
    public record UserCreateInput(string Name, string Pin, Role Role);
    public record UserUpdateInput(string Name, Role Role, bool IsActive, string? Pin);

    private static UserDto ToDto(User u) => new(u.Id, u.Name, u.Role, u.IsActive);

    public static void MapUserAdminEndpoints(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/admin/users").RequireAuthorization(AuthSetup.OwnerPolicy);

        g.MapGet("/", async (AppDb db) =>
            (await db.Users.AsNoTracking().OrderBy(u => u.Name).ToListAsync()).Select(ToDto).ToList());

        g.MapPost("/", async (UserCreateInput input, AppDb db, PinHasher hasher) =>
        {
            var name = await UniqueNameAsync(db, input.Name, null);
            if (!PinHasher.IsValidFormat(input.Pin)) throw AuthEndpoints.PinFormatError();
            if (!Enum.IsDefined(input.Role)) throw new DomainException("rol_gecersiz", "Rol geçersiz.");
            var u = new User { Name = name, PinHash = hasher.Hash(input.Pin), Role = input.Role };
            db.Users.Add(u);
            await db.SaveChangesAsync();
            return ToDto(u);
        });

        g.MapPut("/{id:int}", async (int id, UserUpdateInput input, AppDb db, PinHasher hasher, PinLockout lockout) =>
        {
            var u = await db.Users.FindAsync(id) ?? throw Validate.NotFound();
            if (!Enum.IsDefined(input.Role)) throw new DomainException("rol_gecersiz", "Rol geçersiz.");

            var losesOwner = u is { Role: Role.Owner, IsActive: true } && (input.Role != Role.Owner || !input.IsActive);
            if (losesOwner && !await db.Users.AnyAsync(x => x.Id != id && x.Role == Role.Owner && x.IsActive))
                throw new DomainException("son_sahip", "En az bir aktif sahip hesabı kalmalı.", StatusCodes.Status409Conflict);

            u.Name = await UniqueNameAsync(db, input.Name, id);
            u.Role = input.Role;
            u.IsActive = input.IsActive;
            if (!string.IsNullOrEmpty(input.Pin))
            {
                if (!PinHasher.IsValidFormat(input.Pin)) throw AuthEndpoints.PinFormatError();
                u.PinHash = hasher.Hash(input.Pin);
                lockout.Success(id); // yeni PIN verilince kilit kalkar
            }
            await db.SaveChangesAsync();
            return ToDto(u);
        });
    }

    private static async Task<string> UniqueNameAsync(AppDb db, string? raw, int? selfId)
    {
        var name = Validate.Name(raw, 30);
        if (await db.Users.AnyAsync(u => u.Name == name && u.Id != selfId))
            throw new DomainException("kullanici_adi_var", "Bu adda bir kullanıcı zaten var.", StatusCodes.Status409Conflict);
        return name;
    }
}
```

- [ ] **Step 6: Program.cs'e bağla**

`api.MapPhotoEndpoints();` satırının altına:

```csharp
api.MapTableAdminEndpoints();
api.MapUserAdminEndpoints();
```

- [ ] **Step 7: Testleri çalıştır, geçtiğini gör**

Run: `dotnet test server/Firin.Tests`
Expected: PASS (tüm testler; TableAdminTests 4, UserAdminTests 6)

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: masa ve kullanıcı yönetim API'si"
```

### Task 4: Yönetim arayüzü — ürünler ve kategoriler

**Files:**
- Create: `web/src/api/admin.ts`, `web/src/lib/image.ts`, `web/src/components/Modal.tsx`, `web/src/components/Thumb.tsx`, `web/src/pages/admin/AdminPage.tsx`, `web/src/pages/admin/ProductsAdmin.tsx`, `web/src/pages/admin/ProductForm.tsx`, `web/src/pages/admin/CategoriesAdmin.tsx`
- Modify: `web/src/api/types.ts`, `web/src/lib/format.ts`, `web/src/components/Shell.tsx`, `web/src/styles.css`
- Test: `web/src/lib/format.test.ts`, `web/src/lib/image.test.ts`

**Interfaces:**
- Consumes: Task 1–3 uçları; `api`, `formatKurus`, `parseTlToKurus` (Parça 1)
- Produces:
  - `api/types.ts`: `Product`, `CatalogCategory` (Parça 3 sipariş ekranı kullanır)
  - `api/admin.ts`: `adminApi` nesnesi ve `Category`, `CategoryInput`, `ProductInput`, `TableRow`, `TableInput`, `UserRow`, `UserCreateInput`, `UserUpdateInput` tipleri
  - `lib/format.ts`: `formatPrice(unitType, priceKurus) → "₺45,00 / adet" | "₺600,00 / kg"`
  - `lib/image.ts`: `fitWithin(w, h, max) → { width, height }`, `resizeImage(file, max = 800, quality = 0.85) → Promise<Blob>`
  - `components/Modal.tsx`: `Modal({ title, onClose, children })`
  - `components/Thumb.tsx`: `Thumb({ url, large? })`
  - Query key'ler: `['admin', 'categories']`, `['admin', 'products']`, `['admin', 'tables']`, `['admin', 'users']`, `['catalog']`
  - Rota: `/yonetim/*` (yalnız sahip); sekmeler `urunler`, `kategoriler`, `masalar`, `kullanicilar`. Parça 6 buraya `ayarlar` ekler.

- [ ] **Step 1: Başarısız testleri yaz**

`web/src/lib/format.test.ts` dosyasının sonuna ekle ve üstteki import satırına `formatPrice` ekle:

```ts
describe('formatPrice', () => {
  it('birimiyle birlikte yazar', () => {
    expect(formatPrice('Piece', 4500)).toBe('₺45,00 / adet');
    expect(formatPrice('Gram', 60000)).toBe('₺600,00 / kg');
  });
});
```

`web/src/lib/image.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { fitWithin } from './image';

describe('fitWithin', () => {
  it('uzun kenarı sınıra indirir, oranı korur', () => {
    expect(fitWithin(4000, 3000, 800)).toEqual({ width: 800, height: 600 });
    expect(fitWithin(3000, 4000, 800)).toEqual({ width: 600, height: 800 });
  });

  it('küçük fotoğrafı büyütmez', () => {
    expect(fitWithin(640, 480, 800)).toEqual({ width: 640, height: 480 });
  });
});
```

Run: `cd web && npm test`
Expected: FAIL. `formatPrice` ve `./image` bulunamadı.

- [ ] **Step 2: Yardımcıları yaz**

`web/src/lib/format.ts` sonuna ekle:

```ts
export function formatPrice(unitType: UnitType, priceKurus: number): string {
  return `${formatKurus(priceKurus)} / ${unitType === 'Piece' ? 'adet' : 'kg'}`;
}
```

`web/src/lib/image.ts`:

```ts
export function fitWithin(width: number, height: number, max: number) {
  const scale = Math.min(1, max / Math.max(width, height));
  return { width: Math.round(width * scale), height: Math.round(height * scale) };
}

/** Fotoğrafı en uzun kenarı `max` px olacak şekilde küçültüp JPEG'e çevirir. Telefon fotoğrafları 5–10 MB olabilir. */
export async function resizeImage(file: File, max = 800, quality = 0.85): Promise<Blob> {
  const bitmap = await createImageBitmap(file, { imageOrientation: 'from-image' });
  const { width, height } = fitWithin(bitmap.width, bitmap.height, max);
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  const ctx = canvas.getContext('2d')!;
  ctx.fillStyle = '#ffffff'; // şeffaf PNG'ler JPEG'de siyah olmasın
  ctx.fillRect(0, 0, width, height);
  ctx.drawImage(bitmap, 0, 0, width, height);
  bitmap.close();
  return new Promise((resolve, reject) =>
    canvas.toBlob((b) => (b ? resolve(b) : reject(new Error('Fotoğraf işlenemedi.'))), 'image/jpeg', quality),
  );
}
```

Run: `npm test`
Expected: PASS

- [ ] **Step 3: API tiplerini ve çağrılarını yaz**

`web/src/api/types.ts` sonuna ekle:

```ts
export interface Product {
  id: number;
  categoryId: number;
  name: string;
  unitType: UnitType;
  /** Adetli üründe adet fiyatı, gramlı üründe kilogram fiyatı (kuruş). */
  priceKurus: number;
  photoUrl: string | null;
  isActive: boolean;
  sortOrder: number;
}

export interface CatalogCategory {
  id: number;
  name: string;
  products: Product[];
}
```

`web/src/api/admin.ts`:

```ts
import { api } from './client';
import type { Product, Role, UnitType } from './types';

export interface Category { id: number; name: string; sortOrder: number; productCount: number }
export interface CategoryInput { name: string; sortOrder: number }
export interface ProductInput { categoryId: number; name: string; unitType: UnitType; priceKurus: number; isActive: boolean; sortOrder: number }
export interface TableRow { id: number; name: string; sortOrder: number; isActive: boolean }
export interface TableInput { name: string; sortOrder: number; isActive: boolean }
export interface UserRow { id: number; name: string; role: Role; isActive: boolean }
export interface UserCreateInput { name: string; pin: string; role: Role }
export interface UserUpdateInput { name: string; role: Role; isActive: boolean; pin: string | null }

const post = <T>(path: string, json: unknown) => api<T>(path, { method: 'POST', json });
const put = <T>(path: string, json: unknown) => api<T>(path, { method: 'PUT', json });

export const adminApi = {
  categories: () => api<Category[]>('/admin/categories'),
  createCategory: (i: CategoryInput) => post<Category>('/admin/categories', i),
  updateCategory: (id: number, i: CategoryInput) => put<Category>(`/admin/categories/${id}`, i),
  deleteCategory: (id: number) => api<void>(`/admin/categories/${id}`, { method: 'DELETE' }),

  products: () => api<Product[]>('/admin/products'),
  createProduct: (i: ProductInput) => post<Product>('/admin/products', i),
  updateProduct: (id: number, i: ProductInput) => put<Product>(`/admin/products/${id}`, i),
  uploadPhoto: (id: number, blob: Blob) => {
    const body = new FormData();
    body.append('file', blob, 'foto.jpg');
    return api<Product>(`/admin/products/${id}/photo`, { method: 'POST', body });
  },
  deletePhoto: (id: number) => api<Product>(`/admin/products/${id}/photo`, { method: 'DELETE' }),

  tables: () => api<TableRow[]>('/admin/tables'),
  createTable: (i: TableInput) => post<TableRow>('/admin/tables', i),
  updateTable: (id: number, i: TableInput) => put<TableRow>(`/admin/tables/${id}`, i),

  users: () => api<UserRow[]>('/admin/users'),
  createUser: (i: UserCreateInput) => post<UserRow>('/admin/users', i),
  updateUser: (id: number, i: UserUpdateInput) => put<UserRow>(`/admin/users/${id}`, i),
};
```

- [ ] **Step 4: Ortak bileşenleri yaz**

`web/src/components/Modal.tsx`:

```tsx
import { useEffect, type ReactNode } from 'react';

interface Props {
  title: string;
  onClose: () => void;
  children: ReactNode;
}

export function Modal({ title, onClose, children }: Props) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => e.key === 'Escape' && onClose();
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [onClose]);

  return (
    <div className="modal-backdrop" onClick={onClose}>
      <div className="modal" role="dialog" aria-modal="true" aria-label={title} onClick={(e) => e.stopPropagation()}>
        <div className="modal-head">
          <h2>{title}</h2>
          <button type="button" className="icon" onClick={onClose} aria-label="Kapat">✕</button>
        </div>
        {children}
      </div>
    </div>
  );
}
```

`web/src/components/Thumb.tsx`:

```tsx
export function Thumb({ url, large }: { url: string | null; large?: boolean }) {
  const cls = `thumb${large ? ' large' : ''}`;
  return url ? <img src={url} alt="" className={cls} loading="lazy" /> : <span className={`${cls} placeholder`} aria-hidden>🥐</span>;
}
```

- [ ] **Step 5: CSS ekle**

`web/src/styles.css` sonuna ekle:

```css
input[type='checkbox'], input[type='radio'] { width: 22px; min-height: 22px; height: 22px; margin: 0; accent-color: var(--accent); }
button.icon { min-width: 48px; padding: 0; font-size: 1.2rem; }
.button-like {
  display: inline-flex; align-items: center; justify-content: center; min-height: 48px; padding: 0 16px;
  border: 1px solid var(--border); border-radius: var(--radius); background: var(--surface); cursor: pointer; font-weight: 500;
}

.tabs { display: flex; gap: 4px; overflow-x: auto; margin-bottom: 16px; border-bottom: 1px solid var(--border); }
.tabs a { display: inline-flex; align-items: center; min-height: 48px; padding: 0 14px; color: var(--muted); text-decoration: none; white-space: nowrap; border-bottom: 3px solid transparent; }
.tabs a.active { color: var(--accent); border-bottom-color: var(--accent); font-weight: 600; }

.toolbar { display: flex; flex-wrap: wrap; align-items: center; justify-content: space-between; gap: 8px; margin-bottom: 12px; }
.toolbar h2 { margin: 0; font-size: 1.25rem; }
section h3 { margin: 16px 0 8px; font-size: 1rem; color: var(--muted); }

.admin-list { list-style: none; margin: 0; padding: 0; display: flex; flex-direction: column; gap: 8px; }
.admin-list li.inactive { opacity: 0.55; }
.row-btn { width: 100%; display: flex; align-items: center; gap: 12px; padding: 8px 12px; text-align: left; min-height: 64px; }
.row-main { display: flex; flex-direction: column; flex: 1; min-width: 0; }
.row-main > span { font-weight: 600; overflow: hidden; text-overflow: ellipsis; white-space: nowrap; }
.row-main small { color: var(--muted); }

.thumb { width: 48px; height: 48px; border-radius: 8px; object-fit: cover; flex: none; }
.thumb.large { width: 120px; height: 120px; }
.thumb.placeholder { display: inline-grid; place-items: center; background: var(--busy); font-size: 1.5rem; }
.thumb.large.placeholder { font-size: 3rem; }

.modal-backdrop { position: fixed; inset: 0; z-index: 50; background: rgb(0 0 0 / 0.4); display: grid; place-items: center; padding: 16px; }
.modal { width: min(520px, 100%); max-height: calc(100dvh - 32px); overflow: auto; background: var(--surface); border-radius: var(--radius); padding: 16px; }
.modal-head { display: flex; align-items: center; justify-content: space-between; gap: 8px; margin-bottom: 12px; }
.modal-head h2 { margin: 0; font-size: 1.2rem; }

.segmented { border: 1px solid var(--border); border-radius: var(--radius); padding: 8px 12px; display: flex; flex-wrap: wrap; gap: 16px; margin: 0; }
.segmented legend { padding: 0 4px; font-weight: 500; }
.form label.check, .segmented label { flex-direction: row; align-items: center; gap: 8px; min-height: 48px; font-weight: 400; }
.photo-field { display: flex; flex-wrap: wrap; align-items: center; gap: 12px; }
.form-actions { display: flex; flex-wrap: wrap; gap: 8px; justify-content: space-between; }
```

- [ ] **Step 6: Yönetim sayfası iskeletini ve Shell bağlantısını yaz**

`web/src/pages/admin/AdminPage.tsx`:

```tsx
import { Navigate, NavLink, Route, Routes } from 'react-router';
import { CategoriesAdmin } from './CategoriesAdmin';
import { ProductsAdmin } from './ProductsAdmin';
import { TablesAdmin } from './TablesAdmin';
import { UsersAdmin } from './UsersAdmin';

export function AdminPage() {
  return (
    <div className="page">
      <nav className="tabs">
        <NavLink to="urunler">Ürünler</NavLink>
        <NavLink to="kategoriler">Kategoriler</NavLink>
        <NavLink to="masalar">Masalar</NavLink>
        <NavLink to="kullanicilar">Kullanıcılar</NavLink>
      </nav>
      <Routes>
        <Route index element={<Navigate to="urunler" replace />} />
        <Route path="urunler" element={<ProductsAdmin />} />
        <Route path="kategoriler" element={<CategoriesAdmin />} />
        <Route path="masalar" element={<TablesAdmin />} />
        <Route path="kullanicilar" element={<UsersAdmin />} />
      </Routes>
    </div>
  );
}
```

`TablesAdmin` ve `UsersAdmin` Task 5'te yazılır. Bu task'ın derlenmesi için şimdilik iki küçük dosya oluştur; Task 5 bunların içeriğini değiştirir:

`web/src/pages/admin/TablesAdmin.tsx`:

```tsx
export function TablesAdmin() {
  return <p>Masalar yükleniyor…</p>;
}
```

`web/src/pages/admin/UsersAdmin.tsx`:

```tsx
export function UsersAdmin() {
  return <p>Kullanıcılar yükleniyor…</p>;
}
```

`web/src/components/Shell.tsx` içinde:
- importlara `import { AdminPage } from '../pages/admin/AdminPage';` ekle.
- `{isOwner && null /* Parça 2: Yönetim, Parça 5: Raporlar bağlantıları buraya */}` satırını şununla değiştir:

```tsx
          {isOwner && <NavLink to="/yonetim">Yönetim</NavLink>}
```

- `<Route path="/" element={<HomePage me={me} />} />` satırının altına ekle:

```tsx
          {isOwner && <Route path="/yonetim/*" element={<AdminPage />} />}
```

- [ ] **Step 7: Ürün listesini ve formunu yaz**

`web/src/pages/admin/ProductsAdmin.tsx`:

```tsx
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { Link } from 'react-router';
import { adminApi } from '../../api/admin';
import type { Product } from '../../api/types';
import { Thumb } from '../../components/Thumb';
import { formatPrice } from '../../lib/format';
import { ProductForm } from './ProductForm';

export function ProductsAdmin() {
  const products = useQuery({ queryKey: ['admin', 'products'], queryFn: adminApi.products });
  const categories = useQuery({ queryKey: ['admin', 'categories'], queryFn: adminApi.categories });
  const [editing, setEditing] = useState<Product | 'new' | null>(null);

  if (products.isPending || categories.isPending) return <p>Yükleniyor…</p>;
  if (products.isError || categories.isError) return <p className="error">{(products.error ?? categories.error)!.message}</p>;

  const cats = categories.data;
  if (cats.length === 0)
    return <p>Ürün eklemeden önce <Link to="/yonetim/kategoriler">bir kategori oluşturun</Link> (örn. "Kuru pasta", "İçecekler").</p>;

  return (
    <>
      <div className="toolbar">
        <h2>Ürünler</h2>
        <button className="primary" onClick={() => setEditing('new')}>+ Yeni ürün</button>
      </div>
      {products.data.length === 0 && <p>Henüz ürün yok.</p>}
      {cats.map((c) => {
        const items = products.data.filter((p) => p.categoryId === c.id);
        if (items.length === 0) return null;
        return (
          <section key={c.id}>
            <h3>{c.name}</h3>
            <ul className="admin-list">
              {items.map((p) => (
                <li key={p.id} className={p.isActive ? '' : 'inactive'}>
                  <button className="row-btn" onClick={() => setEditing(p)}>
                    <Thumb url={p.photoUrl} />
                    <span className="row-main">
                      <span>{p.name}</span>
                      <small>{formatPrice(p.unitType, p.priceKurus)}{!p.isActive && ' · satışta değil'}</small>
                    </span>
                  </button>
                </li>
              ))}
            </ul>
          </section>
        );
      })}
      {editing && (
        <ProductForm product={editing === 'new' ? null : editing} categories={cats} onClose={() => setEditing(null)} />
      )}
    </>
  );
}
```

`web/src/pages/admin/ProductForm.tsx`:

```tsx
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useEffect, useRef, useState } from 'react';
import { adminApi, type Category } from '../../api/admin';
import type { Product, UnitType } from '../../api/types';
import { Modal } from '../../components/Modal';
import { Thumb } from '../../components/Thumb';
import { parseTlToKurus } from '../../lib/format';
import { resizeImage } from '../../lib/image';

interface Props {
  product: Product | null;
  categories: Category[];
  onClose: () => void;
}

const toTl = (kurus: number) => (kurus / 100).toFixed(2).replace('.', ',');

export function ProductForm({ product, categories, onClose }: Props) {
  const qc = useQueryClient();
  // Ürün kaydedilip fotoğraf yüklemesi başarısız olursa ikinci "Kaydet" yeni ürün açmasın.
  const savedRef = useRef<Product | null>(product);
  const [name, setName] = useState(product?.name ?? '');
  const [categoryId, setCategoryId] = useState(product?.categoryId ?? categories[0].id);
  const [unitType, setUnitType] = useState<UnitType>(product?.unitType ?? 'Piece');
  const [price, setPrice] = useState(product ? toTl(product.priceKurus) : '');
  const [sortOrder, setSortOrder] = useState(String(product?.sortOrder ?? 0));
  const [isActive, setIsActive] = useState(product?.isActive ?? true);
  const [photo, setPhoto] = useState<Blob | null>(null);
  const [preview, setPreview] = useState<string | null>(product?.photoUrl ?? null);
  const [removePhoto, setRemovePhoto] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => () => {
    if (preview?.startsWith('blob:')) URL.revokeObjectURL(preview);
  }, [preview]);

  const save = useMutation({
    mutationFn: async () => {
      const priceKurus = parseTlToKurus(price);
      if (priceKurus === null) throw new Error('Fiyatı 45 ya da 45,50 biçiminde yazın.');
      const input = { categoryId, name: name.trim(), unitType, priceKurus, isActive, sortOrder: Number(sortOrder) || 0 };
      let saved = savedRef.current
        ? await adminApi.updateProduct(savedRef.current.id, input)
        : await adminApi.createProduct(input);
      savedRef.current = saved;
      if (photo) saved = await adminApi.uploadPhoto(saved.id, photo);
      else if (removePhoto && saved.photoUrl) saved = await adminApi.deletePhoto(saved.id);
      return saved;
    },
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['admin'] });
      qc.invalidateQueries({ queryKey: ['catalog'] });
      onClose();
    },
    onError: (e) => setError(e.message),
  });

  const pickPhoto = async (file: File | undefined) => {
    if (!file) return;
    try {
      const blob = await resizeImage(file);
      setPhoto(blob);
      setRemovePhoto(false);
      setPreview(URL.createObjectURL(blob));
    } catch {
      setError('Fotoğraf okunamadı.');
    }
  };

  return (
    <Modal title={product ? 'Ürünü düzenle' : 'Yeni ürün'} onClose={onClose}>
      <form className="form" onSubmit={(e) => { e.preventDefault(); setError(null); save.mutate(); }}>
        <label>Ürün adı<input value={name} onChange={(e) => setName(e.target.value)} required maxLength={60} autoFocus /></label>
        <label>
          Kategori
          <select value={categoryId} onChange={(e) => setCategoryId(Number(e.target.value))}>
            {categories.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}
          </select>
        </label>
        <fieldset className="segmented">
          <legend>Satış birimi</legend>
          <label><input type="radio" name="unit" checked={unitType === 'Piece'} onChange={() => setUnitType('Piece')} /> Adet</label>
          <label><input type="radio" name="unit" checked={unitType === 'Gram'} onChange={() => setUnitType('Gram')} /> Gram (tartılarak)</label>
        </fieldset>
        <label>
          {unitType === 'Piece' ? 'Adet fiyatı (₺)' : 'Kilogram fiyatı (₺)'}
          <input inputMode="decimal" value={price} onChange={(e) => setPrice(e.target.value)} required
            placeholder={unitType === 'Piece' ? '25,00' : '600,00'} />
        </label>
        <label>Sıra (küçük olan önce görünür)<input type="number" inputMode="numeric" value={sortOrder} onChange={(e) => setSortOrder(e.target.value)} /></label>
        <label className="check"><input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} /> Satışta</label>
        <div className="photo-field">
          <Thumb url={preview} large />
          <label className="button-like">
            Fotoğraf seç
            <input type="file" accept="image/*" hidden onChange={(e) => pickPhoto(e.target.files?.[0])} />
          </label>
          {preview && (
            <button type="button" className="link" onClick={() => { setPhoto(null); setPreview(null); setRemovePhoto(true); }}>
              Fotoğrafı kaldır
            </button>
          )}
        </div>
        {error && <p className="error" role="alert">{error}</p>}
        <button className="primary" disabled={save.isPending}>{save.isPending ? 'Kaydediliyor…' : 'Kaydet'}</button>
      </form>
    </Modal>
  );
}
```

- [ ] **Step 8: Kategori yönetimini yaz**

`web/src/pages/admin/CategoriesAdmin.tsx`:

```tsx
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { adminApi, type Category } from '../../api/admin';
import { Modal } from '../../components/Modal';

export function CategoriesAdmin() {
  const categories = useQuery({ queryKey: ['admin', 'categories'], queryFn: adminApi.categories });
  const [editing, setEditing] = useState<Category | 'new' | null>(null);

  if (categories.isPending) return <p>Yükleniyor…</p>;
  if (categories.isError) return <p className="error">{categories.error.message}</p>;

  return (
    <>
      <div className="toolbar">
        <h2>Kategoriler</h2>
        <button className="primary" onClick={() => setEditing('new')}>+ Yeni kategori</button>
      </div>
      {categories.data.length === 0 && <p>Henüz kategori yok. Örnek: "Kuru pasta", "Börek", "İçecekler".</p>}
      <ul className="admin-list">
        {categories.data.map((c) => (
          <li key={c.id}>
            <button className="row-btn" onClick={() => setEditing(c)}>
              <span className="row-main">
                <span>{c.name}</span>
                <small>{c.productCount} ürün · sıra {c.sortOrder}</small>
              </span>
            </button>
          </li>
        ))}
      </ul>
      {editing && <CategoryForm category={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </>
  );
}

function CategoryForm({ category, onClose }: { category: Category | null; onClose: () => void }) {
  const qc = useQueryClient();
  const [name, setName] = useState(category?.name ?? '');
  const [sortOrder, setSortOrder] = useState(String(category?.sortOrder ?? 0));
  const [error, setError] = useState<string | null>(null);

  const done = () => {
    qc.invalidateQueries({ queryKey: ['admin'] });
    qc.invalidateQueries({ queryKey: ['catalog'] });
    onClose();
  };
  const save = useMutation({
    mutationFn: () => {
      const input = { name: name.trim(), sortOrder: Number(sortOrder) || 0 };
      return category ? adminApi.updateCategory(category.id, input) : adminApi.createCategory(input);
    },
    onSuccess: done,
    onError: (e) => setError(e.message),
  });
  const remove = useMutation({
    mutationFn: () => adminApi.deleteCategory(category!.id),
    onSuccess: done,
    onError: (e) => setError(e.message),
  });

  return (
    <Modal title={category ? 'Kategoriyi düzenle' : 'Yeni kategori'} onClose={onClose}>
      <form className="form" onSubmit={(e) => { e.preventDefault(); setError(null); save.mutate(); }}>
        <label>Kategori adı<input value={name} onChange={(e) => setName(e.target.value)} required maxLength={60} autoFocus /></label>
        <label>Sıra<input type="number" inputMode="numeric" value={sortOrder} onChange={(e) => setSortOrder(e.target.value)} /></label>
        {error && <p className="error" role="alert">{error}</p>}
        <div className="form-actions">
          {category && (
            <button type="button" className="danger" disabled={category.productCount > 0 || remove.isPending}
              title={category.productCount > 0 ? 'İçinde ürün olan kategori silinemez' : undefined}
              onClick={() => remove.mutate()}>
              Sil
            </button>
          )}
          <button className="primary" disabled={save.isPending}>Kaydet</button>
        </div>
      </form>
    </Modal>
  );
}
```

- [ ] **Step 9: Derle ve dene**

```bash
cd "C:/Users/Ali G/Desktop/firin-adisyon/web"
npm test
npm run build
cd ..
dotnet run --project server/Firin.Api
```

Sunucu arka planda çalışırken `http://localhost:5080` adresinde sahip olarak gir:
1. Menüde "Yönetim" görünür. Kategoriler sekmesinde "Kuru pasta" ve "İçecekler" eklenir.
2. Ürünler sekmesinde "Tuzlu kurabiye" eklenir: Gram, kilogram fiyatı `600`. Listede "₺600,00 / kg" yazar.
3. "Çay" eklenir: Adet, `15`. Bir fotoğraf seçilir ve önizleme görünür. Kaydedilince listede küçük resim çıkar. `server/Firin.Api/data/photos/` içindeki dosya 800 px'ten büyük değildir.
4. Ürün düzenlenip "Satışta" işareti kaldırılır. Satır soluklaşır.
5. İçinde ürün olan kategoride "Sil" pasiftir.
6. 394 px genişlikte modal ekrana sığar, yatay kaydırma olmaz.

Sunucuyu durdur.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "feat: ürün ve kategori yönetim ekranları"
```

### Task 5: Yönetim arayüzü — masalar ve kullanıcılar

**Files:**
- Modify: `web/src/pages/admin/TablesAdmin.tsx`, `web/src/pages/admin/UsersAdmin.tsx` (Task 4'teki geçici içeriğin tamamını değiştir), `web/src/styles.css`

**Interfaces:**
- Consumes: `adminApi.tables/createTable/updateTable/users/createUser/updateUser`, `TableRow`, `UserRow` (Task 4); `Modal` (Task 4)
- Produces: masa değişince `['admin', 'tables']` ve `['tables']` (Parça 3 masalar ekranı), kullanıcı değişince `['admin', 'users']` ve `['auth-status']` (giriş ekranı) geçersiz sayılır.

- [ ] **Step 1: Masa yönetimini yaz**

`web/src/pages/admin/TablesAdmin.tsx` (tamamını değiştir):

```tsx
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { adminApi, type TableRow } from '../../api/admin';
import { Modal } from '../../components/Modal';

export function TablesAdmin() {
  const tables = useQuery({ queryKey: ['admin', 'tables'], queryFn: adminApi.tables });
  const [editing, setEditing] = useState<TableRow | 'new' | null>(null);

  if (tables.isPending) return <p>Yükleniyor…</p>;
  if (tables.isError) return <p className="error">{tables.error.message}</p>;

  return (
    <>
      <div className="toolbar">
        <h2>Masalar</h2>
        <div className="toolbar-actions">
          <BulkAdd existing={tables.data} />
          <button className="primary" onClick={() => setEditing('new')}>+ Yeni masa</button>
        </div>
      </div>
      <p className="hint">Tezgâhtan satış için "Tezgâh" adında bir masa açabilirsiniz.</p>
      <ul className="admin-list">
        {tables.data.map((t) => (
          <li key={t.id} className={t.isActive ? '' : 'inactive'}>
            <button className="row-btn" onClick={() => setEditing(t)}>
              <span className="row-main">
                <span>{t.name}</span>
                <small>sıra {t.sortOrder}{!t.isActive && ' · kullanımda değil'}</small>
              </span>
            </button>
          </li>
        ))}
      </ul>
      {editing && <TableForm table={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </>
  );
}

function useInvalidateTables() {
  const qc = useQueryClient();
  return () => {
    qc.invalidateQueries({ queryKey: ['admin', 'tables'] });
    qc.invalidateQueries({ queryKey: ['tables'] });
  };
}

/** "Masa 1", "Masa 2"… adlarından kullanılmayanları sırayla ekler. */
function BulkAdd({ existing }: { existing: TableRow[] }) {
  const invalidate = useInvalidateTables();
  const [count, setCount] = useState('5');
  const [error, setError] = useState<string | null>(null);
  const add = useMutation({
    mutationFn: async () => {
      const n = Math.min(Math.max(Number(count) || 0, 1), 50);
      const names = new Set(existing.map((t) => t.name));
      const maxSort = existing.reduce((m, t) => Math.max(m, t.sortOrder), 0);
      for (let k = 1, added = 0; added < n; k++) {
        const name = `Masa ${k}`;
        if (names.has(name)) continue;
        await adminApi.createTable({ name, sortOrder: maxSort + added + 1, isActive: true });
        added++;
      }
    },
    onSettled: invalidate,
    onError: (e) => setError(e.message),
  });

  return (
    <form className="bulk-add" onSubmit={(e) => { e.preventDefault(); setError(null); add.mutate(); }}>
      <input type="number" min={1} max={50} value={count} onChange={(e) => setCount(e.target.value)} aria-label="Eklenecek masa sayısı" />
      <button disabled={add.isPending}>masa ekle</button>
      {error && <span className="error">{error}</span>}
    </form>
  );
}

function TableForm({ table, onClose }: { table: TableRow | null; onClose: () => void }) {
  const invalidate = useInvalidateTables();
  const [name, setName] = useState(table?.name ?? '');
  const [sortOrder, setSortOrder] = useState(String(table?.sortOrder ?? 0));
  const [isActive, setIsActive] = useState(table?.isActive ?? true);
  const [error, setError] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: () => {
      const input = { name: name.trim(), sortOrder: Number(sortOrder) || 0, isActive };
      return table ? adminApi.updateTable(table.id, input) : adminApi.createTable(input);
    },
    onSuccess: () => { invalidate(); onClose(); },
    onError: (e) => setError(e.message),
  });

  return (
    <Modal title={table ? 'Masayı düzenle' : 'Yeni masa'} onClose={onClose}>
      <form className="form" onSubmit={(e) => { e.preventDefault(); setError(null); save.mutate(); }}>
        <label>Masa adı<input value={name} onChange={(e) => setName(e.target.value)} required maxLength={30} autoFocus /></label>
        <label>Sıra<input type="number" inputMode="numeric" value={sortOrder} onChange={(e) => setSortOrder(e.target.value)} /></label>
        <label className="check"><input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} /> Kullanımda</label>
        {error && <p className="error" role="alert">{error}</p>}
        <button className="primary" disabled={save.isPending}>Kaydet</button>
      </form>
    </Modal>
  );
}
```

- [ ] **Step 2: Kullanıcı yönetimini yaz**

`web/src/pages/admin/UsersAdmin.tsx` (tamamını değiştir):

```tsx
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { adminApi, type UserRow } from '../../api/admin';
import type { Role } from '../../api/types';
import { Modal } from '../../components/Modal';

const roleLabel: Record<Role, string> = { Owner: 'Sahip', Staff: 'Garson' };

export function UsersAdmin() {
  const users = useQuery({ queryKey: ['admin', 'users'], queryFn: adminApi.users });
  const [editing, setEditing] = useState<UserRow | 'new' | null>(null);

  if (users.isPending) return <p>Yükleniyor…</p>;
  if (users.isError) return <p className="error">{users.error.message}</p>;

  return (
    <>
      <div className="toolbar">
        <h2>Kullanıcılar</h2>
        <button className="primary" onClick={() => setEditing('new')}>+ Yeni kullanıcı</button>
      </div>
      <p className="hint">Garsonlar masaya ürün ekleyip hesap kapatabilir. Ürün, fiyat ve raporları yalnızca sahipler görür.</p>
      <ul className="admin-list">
        {users.data.map((u) => (
          <li key={u.id} className={u.isActive ? '' : 'inactive'}>
            <button className="row-btn" onClick={() => setEditing(u)}>
              <span className="row-main">
                <span>{u.name}</span>
                <small>{roleLabel[u.role]}{!u.isActive && ' · giriş kapalı'}</small>
              </span>
            </button>
          </li>
        ))}
      </ul>
      {editing && <UserForm user={editing === 'new' ? null : editing} onClose={() => setEditing(null)} />}
    </>
  );
}

function UserForm({ user, onClose }: { user: UserRow | null; onClose: () => void }) {
  const qc = useQueryClient();
  const [name, setName] = useState(user?.name ?? '');
  const [role, setRole] = useState<Role>(user?.role ?? 'Staff');
  const [isActive, setIsActive] = useState(user?.isActive ?? true);
  const [pin, setPin] = useState('');
  const [error, setError] = useState<string | null>(null);
  const save = useMutation({
    mutationFn: () =>
      user
        ? adminApi.updateUser(user.id, { name: name.trim(), role, isActive, pin: pin || null })
        : adminApi.createUser({ name: name.trim(), role, pin }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: ['admin', 'users'] });
      qc.invalidateQueries({ queryKey: ['auth-status'] });
      onClose();
    },
    onError: (e) => setError(e.message),
  });

  return (
    <Modal title={user ? 'Kullanıcıyı düzenle' : 'Yeni kullanıcı'} onClose={onClose}>
      <form className="form" onSubmit={(e) => { e.preventDefault(); setError(null); save.mutate(); }}>
        <label>Ad<input value={name} onChange={(e) => setName(e.target.value)} required maxLength={30} autoFocus /></label>
        <fieldset className="segmented">
          <legend>Rol</legend>
          <label><input type="radio" name="role" checked={role === 'Staff'} onChange={() => setRole('Staff')} /> Garson</label>
          <label><input type="radio" name="role" checked={role === 'Owner'} onChange={() => setRole('Owner')} /> Sahip</label>
        </fieldset>
        <label>
          {user ? 'Yeni PIN (boş bırakılırsa değişmez)' : 'PIN (4 hane)'}
          <input type="password" inputMode="numeric" pattern="\d{4}" maxLength={4} value={pin}
            onChange={(e) => setPin(e.target.value)} required={!user} autoComplete="new-password" />
        </label>
        {user && <label className="check"><input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} /> Giriş yapabilir</label>}
        {error && <p className="error" role="alert">{error}</p>}
        <button className="primary" disabled={save.isPending}>Kaydet</button>
      </form>
    </Modal>
  );
}
```

- [ ] **Step 3: CSS ekle**

`web/src/styles.css` sonuna ekle:

```css
.toolbar-actions { display: flex; flex-wrap: wrap; gap: 8px; }
.bulk-add { display: flex; align-items: center; gap: 8px; }
.bulk-add input { width: 80px; }
.hint { color: var(--muted); margin: 0 0 12px; }
```

- [ ] **Step 4: Derle ve uçtan uca dene**

```bash
cd "C:/Users/Ali G/Desktop/firin-adisyon/web"
npm run build
cd ..
dotnet run --project server/Firin.Api
```

Sunucu arka planda çalışırken `http://localhost:5080` adresinde sahip olarak gir:
1. Yönetim → Masalar. "5 masa ekle"ye basılınca Masa 1–5 oluşur. Tekrar basılınca Masa 6–10 oluşur.
2. "Tezgâh" adında masa eklenir. Aynı adla ikinci masa eklenmek istenince "Bu adda bir masa zaten var." yazar.
3. Yönetim → Kullanıcılar. Garson "Mehmet", PIN 4321 eklenir.
4. Kendi hesabını Garson yapmayı dene. "En az bir aktif sahip hesabı kalmalı." yazar.
5. "Kullanıcı değiştir" → Mehmet → 4321. Menüde "Yönetim" görünmez. Adres çubuğuna `/yonetim` yazılınca ana sayfaya döner.
6. 394 px genişlikte toolbar alt satıra kayar, yatay kaydırma olmaz.

Sunucuyu durdur.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: masa ve kullanıcı yönetim ekranları"
```
