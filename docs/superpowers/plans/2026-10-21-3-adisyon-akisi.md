# Fırın Adisyon — Parça 3: Adisyon Akışı — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Masalar ekranı ve masa detayı: masaya adetli/gramlı ürün ekleme, miktar düzeltme, kalem iptali. Bir cihazdaki değişiklik diğer cihazlarda anında görünür.

**Architecture:** İş kuralları `OrderService` (scoped) içinde toplanır; uçlar ince kalır. Tutar hesabı saf `Pricing` ve `OrderMath` sınıflarındadır. Her değişiklikten sonra `IRealtime` SignalR ile `TableChanged(tableId)` / `TablesChanged()` / `CatalogChanged()` olaylarını yayar. İstemci bu olaylarla ilgili TanStack Query önbelleğini geçersiz sayar. Mutasyonlar güncel `TableDetailDto` döndürür, böylece işlemi yapan cihaz beklemeden güncellenir.

**Tech Stack:** Parça 1–2'ye ek olarak sunucuda ASP.NET Core SignalR (framework içinde), testte `Microsoft.AspNetCore.SignalR.Client`; web'de `@microsoft/signalr` (Parça 1'de kuruldu).

**Ön koşul:** Parça 1 ve 2 tamamlanmış olmalı.

## Global Constraints

- Hedef `net10.0`, `<Nullable>enable</Nullable>`; Node 24, TypeScript `strict`.
- Para her yerde **kuruş** cinsinden tam sayıdır: C# `long`, TS `number`.
- Gram satır tutarı: `Math.Round(gram * kgFiyatıKuruş / 1000m, MidpointRounding.AwayFromZero)`. Adet: `adet * adetFiyatı`.
- Miktar sınırları: adet 1–999, gram 1–99.999 (tam sayı).
- Zaman her zaman DI'daki `TimeProvider` üzerinden alınır.
- Ürün adı, birimi ve fiyatı kaleme kopyalanır; fiyat değişse açık hesap değişmez.
- Kalem silinmez, iptal işaretlenir (`VoidedAt`, `VoidedById`).
- API JSON camelCase, enum'lar string. Hatalar `DomainException(code, message, status)`.
- Arayüz metinleri Türkçe ve doğru diakritikli. Dokunma hedefleri en az 48 px. 394 px genişlikte yatay kaydırma yok. Tarayıcı `alert/confirm` diyaloğu kullanılmaz.
- Commit adımları: kullanıcı yürütme başında toplu onay vermediyse commit atmadan önce sor. Commit mesajı sonuna `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` eklenir.

## Netleştirilmiş iş kuralları (spec'ten)

- Boş masaya ilk kalem eklenince adisyon açılır. Masa başına tek açık adisyon vardır (veritabanı indeksi + yarış durumunda yeniden okuma).
- Adetli ürün tekrar eklenince aynı satırın adedi artar. Bu yalnızca satır iptal edilmemişse, ödenmemişse ve birim fiyatı aynıysa geçerlidir. Gramlı ürünün her tartımı ayrı satırdır.
- Ödemesi alınmış kalem (`PaymentId` dolu) değiştirilemez ve iptal edilemez. İptal ya da miktar düşürme, toplamı ödenenin altına indiremez.
- Tüm kalemler iptal edilir ve ödeme yoksa adisyon `Cancelled` olur. Kalan tutar 0'a inerse (ve aktif kalem varsa) `Closed` olur.

## Önceki parçalardan kullanılanlar

- `AppDb`, entity'ler (`Order`, `OrderItem`, `Payment`, `DiningTable`, `Product`, enum'lar) — `server/Firin.Api/Data/AppDb.cs`, `server/Firin.Api/Domain/Entities.cs`
- `DomainException`, `ClaimsPrincipal.UserId()`, `AuthSetup.OwnerPolicy`
- Gruplar: `CatalogEndpoints` (`cats`, `products` değişkenleri), `PhotoEndpoints` (`g`), `TableAdminEndpoints` (`g`) — `server/Firin.Api/Endpoints/*.cs`
- Test: `TestApp` (`OwnerAsync`, `StaffAsync`, `SeedUser`, `LoginAsync`, `NewClient`, `Db`, `Clock`), `HttpExt`, `Seed.CategoryAsync/ProductAsync/TableAsync`
- Web: `api`, `ApiError`, `formatKurus`, `formatQuantity`, `formatPrice`, `Modal`, `Thumb`, `Shell`, `Product`, `CatalogCategory` tipleri

## Dosya haritası (bu parça)

| Dosya | Sorumluluk |
|---|---|
| `server/Firin.Api/Orders/Pricing.cs` | Satır tutarı ve miktar doğrulama (saf) |
| `server/Firin.Api/Orders/OrderMath.cs` | Toplam / ödenen / kalan (saf) |
| `server/Firin.Api/Orders/OrderDtos.cs` | Masa ve adisyon DTO'ları |
| `server/Firin.Api/Orders/OrderService.cs` | Kalem ekleme, miktar, iptal, masa listesi |
| `server/Firin.Api/Endpoints/OrderEndpoints.cs` | `/api/tables`, `/api/items` |
| `server/Firin.Api/Realtime/AdisyonHub.cs` | SignalR hub, `IRealtime`, uç filtresi |
| `web/src/realtime/RealtimeProvider.tsx` | Hub bağlantısı, önbellek geçersizleme, bağlantı durumu |
| `web/src/api/orders.ts` | Adisyon tipleri ve çağrıları |
| `web/src/lib/pricing.ts`, `web/src/lib/keypad.ts`, `web/src/lib/time.ts` | Saf yardımcılar |
| `web/src/pages/TablesPage.tsx` | Masa kartları |
| `web/src/pages/table/*.tsx` | Masa detayı: hesap paneli, ürün seçici, gram tuş takımı |

---

### Task 1: Tutar hesabı

**Files:**
- Create: `server/Firin.Api/Orders/Pricing.cs`, `server/Firin.Api/Orders/OrderMath.cs`
- Test: `server/Firin.Tests/PricingTests.cs`

**Interfaces:**
- Produces: `Pricing.MaxPieces = 999`, `Pricing.MaxGrams = 99_999`, `Pricing.LineTotal(UnitType unit, long unitPriceKurus, int quantity) → long`, `Pricing.ValidateQuantity(UnitType unit, int quantity)` (geçersizse `DomainException("miktar_gecersiz")`); `OrderMath.Total(Order) → long` (iptal edilmemiş kalemler), `OrderMath.Paid(Order) → long`, `OrderMath.Remaining(Order) → long`

- [ ] **Step 1: Başarısız testleri yaz**

`server/Firin.Tests/PricingTests.cs`:

```csharp
using Firin.Api;
using Firin.Api.Domain;
using Firin.Api.Orders;

namespace Firin.Tests;

public class PricingTests
{
    [Theory]
    [InlineData(UnitType.Piece, 2500, 3, 7500)]
    [InlineData(UnitType.Gram, 60000, 250, 15000)]   // 600 ₺/kg × 250 g = 150 ₺
    [InlineData(UnitType.Gram, 45000, 333, 14985)]   // 149,85 ₺
    [InlineData(UnitType.Gram, 1234, 125, 154)]      // 154,25 → 154
    [InlineData(UnitType.Gram, 1000, 500, 500)]
    [InlineData(UnitType.Gram, 1001, 500, 501)]      // 500,5 → 501 (yarım yukarı)
    [InlineData(UnitType.Gram, 99_999, 99_999, 9_999_800)]
    public void LineTotal_is_computed_in_kurus(UnitType unit, long price, int qty, long expected)
    {
        Assert.Equal(expected, Pricing.LineTotal(unit, price, qty));
    }

    [Theory]
    [InlineData(UnitType.Piece, 0)]
    [InlineData(UnitType.Piece, 1000)]
    [InlineData(UnitType.Gram, 0)]
    [InlineData(UnitType.Gram, 100_000)]
    [InlineData(UnitType.Gram, -5)]
    public void Invalid_quantity_throws(UnitType unit, int qty)
    {
        var ex = Assert.Throws<DomainException>(() => Pricing.ValidateQuantity(unit, qty));
        Assert.Equal("miktar_gecersiz", ex.Code);
    }

    [Fact]
    public void OrderMath_ignores_voided_items()
    {
        var order = new Order
        {
            Items =
            [
                new OrderItem { ProductName = "Çay", LineTotalKurus = 3000 },
                new OrderItem { ProductName = "Simit", LineTotalKurus = 2000, VoidedAt = DateTime.UtcNow },
                new OrderItem { ProductName = "Kurabiye", LineTotalKurus = 15000 },
            ],
            Payments = [new Payment { AmountKurus = 5000 }],
        };
        Assert.Equal(18000, OrderMath.Total(order));
        Assert.Equal(5000, OrderMath.Paid(order));
        Assert.Equal(13000, OrderMath.Remaining(order));
    }
}
```

- [ ] **Step 2: Testleri çalıştır, başarısız olduğunu gör**

Run: `dotnet test server/Firin.Tests --filter PricingTests`
Expected: FAIL. `Firin.Api.Orders` bulunamadığı için derleme hatası.

- [ ] **Step 3: Uygulamayı yaz**

`server/Firin.Api/Orders/Pricing.cs`:

```csharp
using Firin.Api.Domain;

namespace Firin.Api.Orders;

public static class Pricing
{
    public const int MaxPieces = 999;
    public const int MaxGrams = 99_999;

    /// <summary>Adetli: adet × adet fiyatı. Gramlı: gram × kg fiyatı / 1000, kuruşa yarım yukarı yuvarlanır.</summary>
    public static long LineTotal(UnitType unit, long unitPriceKurus, int quantity) => unit switch
    {
        UnitType.Piece => unitPriceKurus * quantity,
        UnitType.Gram => (long)Math.Round(quantity * (decimal)unitPriceKurus / 1000m, MidpointRounding.AwayFromZero),
        _ => throw new ArgumentOutOfRangeException(nameof(unit)),
    };

    public static void ValidateQuantity(UnitType unit, int quantity)
    {
        var max = unit == UnitType.Piece ? MaxPieces : MaxGrams;
        if (quantity < 1 || quantity > max)
            throw new DomainException("miktar_gecersiz", unit == UnitType.Piece
                ? $"Adet 1 ile {MaxPieces} arasında olmalı."
                : "Gram 1 ile 99.999 arasında olmalı.");
    }
}
```

`server/Firin.Api/Orders/OrderMath.cs`:

```csharp
using Firin.Api.Domain;

namespace Firin.Api.Orders;

public static class OrderMath
{
    public static long Total(Order o) => o.Items.Where(i => i.VoidedAt is null).Sum(i => i.LineTotalKurus);
    public static long Paid(Order o) => o.Payments.Sum(p => p.AmountKurus);
    public static long Remaining(Order o) => Total(o) - Paid(o);
}
```

- [ ] **Step 4: Testleri çalıştır, geçtiğini gör**

Run: `dotnet test server/Firin.Tests --filter PricingTests`
Expected: PASS (13 test)

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: satır tutarı ve adisyon toplamı hesabı"
```

### Task 2: SignalR hub ve değişiklik yayını

**Files:**
- Create: `server/Firin.Api/Realtime/AdisyonHub.cs`
- Modify: `server/Firin.Api/Program.cs`, `server/Firin.Api/Endpoints/CatalogEndpoints.cs`, `server/Firin.Api/Endpoints/PhotoEndpoints.cs`, `server/Firin.Api/Endpoints/TableAdminEndpoints.cs`, `server/Firin.Tests/TestApp.cs`
- Test: `server/Firin.Tests/RealtimeTests.cs`

**Interfaces:**
- Produces:
  - Hub: `/hubs/adisyon`, oturum ister. İstemciye giden olaylar: `"TableChanged"(int tableId)`, `"TablesChanged"()`, `"CatalogChanged"()`
  - `IRealtime` (singleton): `Task TableChanged(int tableId)`, `Task TablesChanged()`, `Task CatalogChanged()`
  - `RouteGroupBuilder.NotifiesOnChange(Func<IRealtime, Task> notify)`: GET dışındaki başarılı isteklerden sonra `notify` çağırır
  - Test: `TestApp.LoginCookieAsync(int userId, string pin) → string` (`"firin_oturum=..."`), `TestApp.HubAsync(string cookie) → HubConnection` (başlatılmış)

- [ ] **Step 1: Test paketini ekle ve yardımcıları yaz**

```bash
dotnet add server/Firin.Tests package Microsoft.AspNetCore.SignalR.Client
```

`server/Firin.Tests/TestApp.cs` başına ekle:

```csharp
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
```

`TestApp` sınıfına ekle:

```csharp
    /// <summary>Hub bağlantısı için oturum çerezini elle alır.</summary>
    public async Task<string> LoginCookieAsync(int userId, string pin)
    {
        using var c = Factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false });
        var res = await c.PostAsJsonAsync("/api/auth/login", new { userId, pin });
        res.EnsureSuccessStatusCode();
        return res.Headers.GetValues("Set-Cookie").First().Split(';')[0];
    }

    public async Task<HubConnection> HubAsync(string cookie)
    {
        var conn = new HubConnectionBuilder()
            .WithUrl(new Uri(Factory.Server.BaseAddress, "hubs/adisyon"), o =>
            {
                o.HttpMessageHandlerFactory = _ => Factory.Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling;
                o.Headers["Cookie"] = cookie;
            })
            .Build();
        await conn.StartAsync();
        return conn;
    }
```

- [ ] **Step 2: Başarısız testleri yaz**

`server/Firin.Tests/RealtimeTests.cs`:

```csharp
using Firin.Api.Domain;
using Microsoft.AspNetCore.SignalR.Client;

namespace Firin.Tests;

public sealed class RealtimeTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task Hub_requires_login()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => _app.HubAsync(cookie: ""));
    }

    [Fact]
    public async Task Table_admin_change_is_broadcast()
    {
        var id = _app.SeedUser("Sahip", "1234", Role.Owner);
        var owner = await _app.LoginAsync(id, "1234");
        await using var hub = await _app.HubAsync(await _app.LoginCookieAsync(id, "1234"));
        var got = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On("TablesChanged", () => got.TrySetResult());

        await Seed.TableAsync(owner, "Masa 1");
        await got.Task.WaitAsync(Wait);
    }

    [Fact]
    public async Task Catalog_change_is_broadcast_but_reads_are_not()
    {
        var id = _app.SeedUser("Sahip", "1234", Role.Owner);
        var owner = await _app.LoginAsync(id, "1234");
        await using var hub = await _app.HubAsync(await _app.LoginCookieAsync(id, "1234"));
        var count = 0;
        var got = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On("CatalogChanged", () => { Interlocked.Increment(ref count); got.TrySetResult(); });

        await owner.GetAsync("/api/admin/categories");
        await Seed.CategoryAsync(owner, "Börek");
        await got.Task.WaitAsync(Wait);
        Assert.Equal(1, count);
    }
}
```

- [ ] **Step 3: Testleri çalıştır, başarısız olduğunu gör**

Run: `dotnet test server/Firin.Tests --filter RealtimeTests`
Expected: FAIL. `Hub_requires_login` geçebilir (hub yok, 404 ile başlatma hatası). Diğer ikisi olay gelmediği için 10 saniyede `TimeoutException` ile düşer.

- [ ] **Step 4: Hub'ı ve yayın servisini yaz**

`server/Firin.Api/Realtime/AdisyonHub.cs`:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Firin.Api.Realtime;

/// <summary>İstemciler yalnızca dinler; sunucuya mesaj göndermezler.</summary>
[Authorize]
public sealed class AdisyonHub : Hub;

public interface IRealtime
{
    Task TableChanged(int tableId);
    Task TablesChanged();
    Task CatalogChanged();
}

public sealed class SignalRRealtime(IHubContext<AdisyonHub> hub) : IRealtime
{
    public Task TableChanged(int tableId) => hub.Clients.All.SendAsync("TableChanged", tableId);
    public Task TablesChanged() => hub.Clients.All.SendAsync("TablesChanged");
    public Task CatalogChanged() => hub.Clients.All.SendAsync("CatalogChanged");
}

public static class RealtimeExtensions
{
    /// <summary>Gruptaki GET dışı istekler başarıyla bitince yayın yapar. DomainException fırlarsa yayın olmaz.</summary>
    public static RouteGroupBuilder NotifiesOnChange(this RouteGroupBuilder group, Func<IRealtime, Task> notify)
    {
        group.AddEndpointFilter(async (ctx, next) =>
        {
            var result = await next(ctx);
            if (!HttpMethods.IsGet(ctx.HttpContext.Request.Method))
                await notify(ctx.HttpContext.RequestServices.GetRequiredService<IRealtime>());
            return result;
        });
        return group;
    }
}
```

- [ ] **Step 5: Program.cs'e bağla**

Başa `using Firin.Api.Realtime;` ekle.

`builder.Services.AddFirinAuth();` satırının altına:

```csharp
builder.Services.AddSignalR();
builder.Services.AddSingleton<IRealtime, SignalRRealtime>();
```

`app.MapFallbackToFile("index.html");` satırının üstüne:

```csharp
app.MapHub<AdisyonHub>("/hubs/adisyon");
```

- [ ] **Step 6: Yönetim gruplarına yayını ekle**

Her dosyanın başına `using Firin.Api.Realtime;` ekle ve grup tanımlarının sonuna `.NotifiesOnChange(...)` ekle:

`server/Firin.Api/Endpoints/CatalogEndpoints.cs`:

```csharp
        var cats = api.MapGroup("/admin/categories").RequireAuthorization(AuthSetup.OwnerPolicy)
            .NotifiesOnChange(r => r.CatalogChanged());
```

```csharp
        var products = api.MapGroup("/admin/products").RequireAuthorization(AuthSetup.OwnerPolicy)
            .NotifiesOnChange(r => r.CatalogChanged());
```

`server/Firin.Api/Endpoints/PhotoEndpoints.cs`:

```csharp
        var g = api.MapGroup("/admin/products/{id:int}/photo").RequireAuthorization(AuthSetup.OwnerPolicy)
            .NotifiesOnChange(r => r.CatalogChanged());
```

`server/Firin.Api/Endpoints/TableAdminEndpoints.cs`:

```csharp
        var g = api.MapGroup("/admin/tables").RequireAuthorization(AuthSetup.OwnerPolicy)
            .NotifiesOnChange(r => r.TablesChanged());
```

- [ ] **Step 7: Testleri çalıştır, geçtiğini gör**

Run: `dotnet test server/Firin.Tests`
Expected: PASS (tüm testler; RealtimeTests 3 test)

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: SignalR ile katalog ve masa değişikliği yayını"
```

### Task 3: Adisyon servisi ve uçları

**Files:**
- Create: `server/Firin.Api/Orders/OrderDtos.cs`, `server/Firin.Api/Orders/OrderService.cs`, `server/Firin.Api/Endpoints/OrderEndpoints.cs`
- Modify: `server/Firin.Api/Program.cs`
- Test: `server/Firin.Tests/OrderTests.cs`

**Interfaces:**
- Consumes: `Pricing`, `OrderMath` (Task 1); `IRealtime` (Task 2)
- Produces:
  - DTO'lar (`Firin.Api.Orders`): `OrderItemDto(int Id, int ProductId, string ProductName, UnitType UnitType, long UnitPriceKurus, int Quantity, long LineTotalKurus, bool IsPaid, DateTime AddedAt, string AddedBy)`, `PaymentDto(int Id, long AmountKurus, PaymentMethod Method, DateTime PaidAt, string UserName)`, `OrderDto(int Id, OrderStatus Status, DateTime OpenedAt, long TotalKurus, long PaidKurus, long RemainingKurus, List<OrderItemDto> Items, List<PaymentDto> Payments)` (Items yalnızca iptal edilmemiş kalemler), `TableDetailDto(int Id, string Name, OrderDto? Order)` (Order = açık adisyon ya da null), `TableSummaryDto(int Id, string Name, int? OrderId, long TotalKurus, long RemainingKurus, DateTime? OpenedAt, int ItemCount)`
  - `OrderService` (scoped): `ListTablesAsync()`, `GetTableAsync(int tableId)`, `AddItemAsync(int tableId, int productId, int quantity, int userId)`, `SetQuantityAsync(int itemId, int quantity, int userId)`, `VoidItemAsync(int itemId, int userId)`; Parça 4 için: `LoadOpenOrderAsync(int tableId) → Task<Order?>`, `CloseIfSettled(Order order, int userId)`, `Now`
  - Uçlar (oturum gerekli, her rol): `GET /api/tables → List<TableSummaryDto>`, `GET /api/tables/{id} → TableDetailDto`, `POST /api/tables/{id}/items (AddItemRequest(ProductId, Quantity)) → TableDetailDto`, `PUT /api/items/{id} (SetQuantityRequest(Quantity)) → TableDetailDto`, `DELETE /api/items/{id} → TableDetailDto`
  - Hata kodları: `masa_yok` (404), `masa_pasif`, `urun_yok` (404), `urun_pasif`, `miktar_gecersiz`, `kalem_yok` (404), `hesap_kapali` (409), `kalem_iptal` (409), `kalem_odendi` (409), `odenenden_az` (409)

- [ ] **Step 1: Başarısız testleri yaz**

`server/Firin.Tests/OrderTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Firin.Api.Domain;
using Firin.Api.Orders;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using static Firin.Api.Endpoints.CatalogEndpoints;
using static Firin.Api.Endpoints.TableAdminEndpoints;

namespace Firin.Tests;

public sealed class OrderTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    private HttpClient _owner = null!;
    private HttpClient _staff = null!;
    private TableDto _table = null!;
    private ProductDto _tea = null!;     // 15 ₺ / adet
    private ProductDto _cookie = null!;  // 600 ₺ / kg

    private async Task SetupAsync()
    {
        _owner = await _app.OwnerAsync();
        var cat = await Seed.CategoryAsync(_owner, "Genel");
        _tea = await Seed.ProductAsync(_owner, cat.Id, "Çay", UnitType.Piece, 1500);
        _cookie = await Seed.ProductAsync(_owner, cat.Id, "Tuzlu kurabiye", UnitType.Gram, 60000);
        _table = await Seed.TableAsync(_owner, "Masa 1");
        _staff = await _app.StaffAsync();
    }

    private Task<HttpResponseMessage> Add(int productId, int quantity, int? tableId = null) =>
        _staff.PostAsJsonAsync($"/api/tables/{tableId ?? _table.Id}/items", new { productId, quantity });

    private async Task<TableDetailDto> AddOk(int productId, int quantity)
    {
        var res = await Add(productId, quantity);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return await res.ReadAs<TableDetailDto>();
    }

    private async Task<TableDetailDto> Detail() => await (await _staff.GetAsync($"/api/tables/{_table.Id}")).ReadAs<TableDetailDto>();

    private int SeedPayment(long amount, bool linkAllItems = false) => _app.Db(db =>
    {
        var order = db.Orders.Include(o => o.Items).Single(o => o.TableId == _table.Id && o.Status == OrderStatus.Open);
        var p = new Payment { OrderId = order.Id, AmountKurus = amount, Method = PaymentMethod.Cash, PaidAt = _app.Clock.GetUtcNow().UtcDateTime, UserId = order.OpenedById };
        db.Payments.Add(p);
        db.SaveChanges();
        if (linkAllItems)
        {
            foreach (var i in order.Items) i.PaymentId = p.Id;
            db.SaveChanges();
        }
        return p.Id;
    });

    [Fact]
    public async Task Empty_table_has_no_order()
    {
        await SetupAsync();
        Assert.Null((await Detail()).Order);
        var list = await (await _staff.GetAsync("/api/tables")).ReadAs<List<TableSummaryDto>>();
        var t = Assert.Single(list);
        Assert.Null(t.OrderId);
        Assert.Equal(0, t.TotalKurus);
    }

    [Fact]
    public async Task Adding_items_opens_order_and_computes_total()
    {
        await SetupAsync();
        await AddOk(_tea.Id, 2);
        var d = await AddOk(_cookie.Id, 250);

        Assert.NotNull(d.Order);
        Assert.Equal(3000 + 15000, d.Order.TotalKurus);
        Assert.Equal(18000, d.Order.RemainingKurus);
        Assert.Equal(2, d.Order.Items.Count);
        Assert.Equal("Garson", d.Order.Items[0].AddedBy);

        var t = Assert.Single(await (await _staff.GetAsync("/api/tables")).ReadAs<List<TableSummaryDto>>());
        Assert.Equal(d.Order.Id, t.OrderId);
        Assert.Equal(18000, t.TotalKurus);
        Assert.Equal(2, t.ItemCount);
        Assert.Equal(_app.Clock.GetUtcNow().UtcDateTime, t.OpenedAt!.Value);
    }

    [Fact]
    public async Task Same_piece_product_is_merged_into_one_line()
    {
        await SetupAsync();
        await AddOk(_tea.Id, 1);
        var d = await AddOk(_tea.Id, 2);
        var line = Assert.Single(d.Order!.Items);
        Assert.Equal(3, line.Quantity);
        Assert.Equal(4500, line.LineTotalKurus);
    }

    [Fact]
    public async Task Each_weighing_is_a_separate_line()
    {
        await SetupAsync();
        await AddOk(_cookie.Id, 250);
        var d = await AddOk(_cookie.Id, 300);
        Assert.Equal(new[] { 15000L, 18000L }, d.Order!.Items.Select(i => i.LineTotalKurus).ToArray());
    }

    [Fact]
    public async Task Price_change_does_not_touch_open_order()
    {
        await SetupAsync();
        await AddOk(_tea.Id, 1);
        await _owner.PutAsJsonAsync($"/api/admin/products/{_tea.Id}",
            new { categoryId = _tea.CategoryId, name = "Çay", unitType = "Piece", priceKurus = 2000, isActive = true, sortOrder = 0 });

        Assert.Equal(1500, Assert.Single((await Detail()).Order!.Items).LineTotalKurus);

        var d = await AddOk(_tea.Id, 1); // yeni fiyat yeni satır açar
        Assert.Equal(new[] { 1500L, 2000L }, d.Order!.Items.Select(i => i.UnitPriceKurus).ToArray());
    }

    [Fact]
    public async Task Set_quantity_recomputes_line()
    {
        await SetupAsync();
        var item = Assert.Single((await AddOk(_cookie.Id, 250)).Order!.Items);
        var res = await _staff.PutAsJsonAsync($"/api/items/{item.Id}", new { quantity = 500 });
        var d = await res.ReadAs<TableDetailDto>();
        Assert.Equal(30000, d.Order!.TotalKurus);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1000)]
    [InlineData(true, 100_000)]
    public async Task Invalid_quantity_is_rejected(bool gram, int qty)
    {
        await SetupAsync();
        var res = await Add(gram ? _cookie.Id : _tea.Id, qty);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("miktar_gecersiz", await res.ProblemCode());
    }

    [Fact]
    public async Task Merging_beyond_max_pieces_is_rejected()
    {
        await SetupAsync();
        await AddOk(_tea.Id, 998);
        Assert.Equal("miktar_gecersiz", await (await Add(_tea.Id, 2)).ProblemCode());
    }

    [Fact]
    public async Task Inactive_product_and_unknown_table_are_rejected()
    {
        await SetupAsync();
        await _owner.PutAsJsonAsync($"/api/admin/products/{_tea.Id}",
            new { categoryId = _tea.CategoryId, name = "Çay", unitType = "Piece", priceKurus = 1500, isActive = false, sortOrder = 0 });
        Assert.Equal("urun_pasif", await (await Add(_tea.Id, 1)).ProblemCode());

        var res = await Add(_cookie.Id, 100, tableId: 999);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("masa_yok", await res.ProblemCode());
    }

    [Fact]
    public async Task Voiding_last_item_cancels_order_but_keeps_record()
    {
        await SetupAsync();
        var item = Assert.Single((await AddOk(_tea.Id, 1)).Order!.Items);
        var d = await (await _staff.DeleteAsync($"/api/items/{item.Id}")).ReadAs<TableDetailDto>();
        Assert.Null(d.Order);

        var (status, voidedBy) = _app.Db(db =>
        {
            var o = db.Orders.Include(x => x.Items).Single();
            return (o.Status, o.Items.Single().VoidedById);
        });
        Assert.Equal(OrderStatus.Cancelled, status);
        Assert.NotNull(voidedBy);
    }

    [Fact]
    public async Task Voided_item_cannot_be_voided_again()
    {
        await SetupAsync();
        await AddOk(_tea.Id, 1);
        var item = (await AddOk(_cookie.Id, 100)).Order!.Items[1];
        await _staff.DeleteAsync($"/api/items/{item.Id}");
        Assert.Equal("kalem_iptal", await (await _staff.DeleteAsync($"/api/items/{item.Id}")).ProblemCode());
    }

    [Fact]
    public async Task Paid_item_cannot_be_changed_or_voided()
    {
        await SetupAsync();
        await AddOk(_tea.Id, 1);
        var item = Assert.Single((await AddOk(_cookie.Id, 250)).Order!.Items, i => i.ProductId == _cookie.Id);
        await AddOk(_tea.Id, 1); // açık kalsın diye ödenmeyen kalem
        SeedPayment(15000);
        _app.Db(db => { db.OrderItems.Single(i => i.Id == item.Id).PaymentId = db.Payments.Single().Id; return db.SaveChanges(); });

        Assert.Equal("kalem_odendi", await (await _staff.PutAsJsonAsync($"/api/items/{item.Id}", new { quantity = 100 })).ProblemCode());
        Assert.Equal("kalem_odendi", await (await _staff.DeleteAsync($"/api/items/{item.Id}")).ProblemCode());
        Assert.True((await Detail()).Order!.Items.Single(i => i.Id == item.Id).IsPaid);
    }

    [Fact]
    public async Task Total_cannot_drop_below_paid_amount()
    {
        await SetupAsync();
        await AddOk(_tea.Id, 2);                      // 30 ₺
        var d = await AddOk(_cookie.Id, 250);         // 150 ₺
        SeedPayment(16000);
        var cookieLine = d.Order!.Items.Single(i => i.ProductId == _cookie.Id);

        var res = await _staff.DeleteAsync($"/api/items/{cookieLine.Id}");
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("odenenden_az", await res.ProblemCode());
    }

    [Fact]
    public async Task Reaching_paid_amount_closes_order_and_next_item_opens_new_one()
    {
        await SetupAsync();
        await AddOk(_tea.Id, 2);                      // 30 ₺
        var d = await AddOk(_cookie.Id, 250);         // 150 ₺
        var firstOrderId = d.Order!.Id;
        SeedPayment(3000);

        var cookieLine = d.Order.Items.Single(i => i.ProductId == _cookie.Id);
        var after = await (await _staff.DeleteAsync($"/api/items/{cookieLine.Id}")).ReadAs<TableDetailDto>();
        Assert.Null(after.Order);
        Assert.Equal(OrderStatus.Closed, _app.Db(db => db.Orders.Single(o => o.Id == firstOrderId).Status));

        var next = await AddOk(_tea.Id, 1);
        Assert.NotEqual(firstOrderId, next.Order!.Id);
    }

    [Fact]
    public async Task Inactive_tables_are_not_listed()
    {
        await SetupAsync();
        await _owner.PutAsJsonAsync($"/api/admin/tables/{_table.Id}", new { name = "Masa 1", sortOrder = 0, isActive = false });
        Assert.Empty(await (await _staff.GetAsync("/api/tables")).ReadAs<List<TableSummaryDto>>());
        Assert.Equal("masa_pasif", await (await Add(_tea.Id, 1)).ProblemCode());
    }

    [Fact]
    public async Task Anonymous_cannot_read_tables()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.Client.GetAsync("/api/tables")).StatusCode);
    }

    [Fact]
    public async Task Adding_item_broadcasts_table_changed()
    {
        await SetupAsync();
        var id = _app.SeedUser("Kasa", "1111", Role.Staff);
        await using var hub = await _app.HubAsync(await _app.LoginCookieAsync(id, "1111"));
        var got = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<int>("TableChanged", t => got.TrySetResult(t));

        await AddOk(_tea.Id, 1);
        Assert.Equal(_table.Id, await got.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }
}
```

- [ ] **Step 2: Testleri çalıştır, başarısız olduğunu gör**

Run: `dotnet test server/Firin.Tests --filter OrderTests`
Expected: FAIL. `TableDetailDto` / `TableSummaryDto` bulunamadığı için derleme hatası.

- [ ] **Step 3: DTO'ları yaz**

`server/Firin.Api/Orders/OrderDtos.cs`:

```csharp
using Firin.Api.Domain;

namespace Firin.Api.Orders;

public record OrderItemDto(int Id, int ProductId, string ProductName, UnitType UnitType, long UnitPriceKurus,
    int Quantity, long LineTotalKurus, bool IsPaid, DateTime AddedAt, string AddedBy);

public record PaymentDto(int Id, long AmountKurus, PaymentMethod Method, DateTime PaidAt, string UserName);

public record OrderDto(int Id, OrderStatus Status, DateTime OpenedAt, long TotalKurus, long PaidKurus,
    long RemainingKurus, List<OrderItemDto> Items, List<PaymentDto> Payments);

/// <summary>Order: masanın açık adisyonu; yoksa null.</summary>
public record TableDetailDto(int Id, string Name, OrderDto? Order);

public record TableSummaryDto(int Id, string Name, int? OrderId, long TotalKurus, long RemainingKurus,
    DateTime? OpenedAt, int ItemCount);
```

- [ ] **Step 4: Servisi yaz**

`server/Firin.Api/Orders/OrderService.cs`:

```csharp
using Firin.Api.Data;
using Firin.Api.Domain;
using Firin.Api.Realtime;
using Microsoft.EntityFrameworkCore;

namespace Firin.Api.Orders;

public sealed class OrderService(AppDb db, TimeProvider clock, IRealtime realtime)
{
    public DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<List<TableSummaryDto>> ListTablesAsync()
    {
        var tables = await db.Tables.AsNoTracking().Where(t => t.IsActive)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync();
        var open = await db.Orders.AsNoTracking().Where(o => o.Status == OrderStatus.Open)
            .Select(o => new
            {
                o.Id,
                o.TableId,
                o.OpenedAt,
                Total = o.Items.Where(i => i.VoidedAt == null).Sum(i => i.LineTotalKurus),
                Paid = o.Payments.Sum(p => p.AmountKurus),
                Count = o.Items.Count(i => i.VoidedAt == null),
            })
            .ToListAsync();
        var byTable = open.ToDictionary(o => o.TableId);
        return tables.Select(t => byTable.TryGetValue(t.Id, out var o)
                ? new TableSummaryDto(t.Id, t.Name, o.Id, o.Total, o.Total - o.Paid, o.OpenedAt, o.Count)
                : new TableSummaryDto(t.Id, t.Name, null, 0, 0, null, 0))
            .ToList();
    }

    public async Task<TableDetailDto> GetTableAsync(int tableId)
    {
        var table = await db.Tables.FirstOrDefaultAsync(t => t.Id == tableId) ?? throw TableNotFound();
        var order = await LoadOpenOrderAsync(tableId);
        return new TableDetailDto(table.Id, table.Name, order is null ? null : await ToDtoAsync(order));
    }

    public Task<Order?> LoadOpenOrderAsync(int tableId) => db.Orders
        .Include(o => o.Items).Include(o => o.Payments)
        .FirstOrDefaultAsync(o => o.TableId == tableId && o.Status == OrderStatus.Open);

    public async Task<TableDetailDto> AddItemAsync(int tableId, int productId, int quantity, int userId)
    {
        var table = await db.Tables.FirstOrDefaultAsync(t => t.Id == tableId) ?? throw TableNotFound();
        if (!table.IsActive) throw new DomainException("masa_pasif", "Bu masa kullanımda değil.");
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId)
            ?? throw new DomainException("urun_yok", "Ürün bulunamadı.", StatusCodes.Status404NotFound);
        if (!product.IsActive) throw new DomainException("urun_pasif", "Bu ürün satışta değil.");
        Pricing.ValidateQuantity(product.UnitType, quantity);

        var order = await LoadOpenOrderAsync(tableId) ?? await OpenOrderAsync(tableId, userId);

        var merge = product.UnitType == UnitType.Piece
            ? order.Items.FirstOrDefault(i => i.ProductId == productId && i.VoidedAt == null
                && i.PaymentId == null && i.UnitPriceKurus == product.PriceKurus)
            : null;
        if (merge is not null)
        {
            var q = merge.Quantity + quantity;
            Pricing.ValidateQuantity(UnitType.Piece, q);
            merge.Quantity = q;
            merge.LineTotalKurus = Pricing.LineTotal(UnitType.Piece, merge.UnitPriceKurus, q);
        }
        else
        {
            order.Items.Add(new OrderItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                UnitType = product.UnitType,
                UnitPriceKurus = product.PriceKurus,
                Quantity = quantity,
                LineTotalKurus = Pricing.LineTotal(product.UnitType, product.PriceKurus, quantity),
                AddedById = userId,
                AddedAt = Now,
            });
        }
        await db.SaveChangesAsync();
        await realtime.TableChanged(tableId);
        return await GetTableAsync(tableId);
    }

    public async Task<TableDetailDto> SetQuantityAsync(int itemId, int quantity, int userId)
    {
        var (order, item) = await LoadEditableItemAsync(itemId);
        Pricing.ValidateQuantity(item.UnitType, quantity);
        item.Quantity = quantity;
        item.LineTotalKurus = Pricing.LineTotal(item.UnitType, item.UnitPriceKurus, quantity);
        EnsureNotBelowPaid(order);
        CloseIfSettled(order, userId);
        await db.SaveChangesAsync();
        await realtime.TableChanged(order.TableId);
        return await GetTableAsync(order.TableId);
    }

    public async Task<TableDetailDto> VoidItemAsync(int itemId, int userId)
    {
        var (order, item) = await LoadEditableItemAsync(itemId);
        item.VoidedAt = Now;
        item.VoidedById = userId;
        EnsureNotBelowPaid(order);
        if (!order.Items.Any(i => i.VoidedAt == null) && order.Payments.Count == 0)
        {
            order.Status = OrderStatus.Cancelled;
            order.ClosedAt = Now;
            order.ClosedById = userId;
        }
        else
        {
            CloseIfSettled(order, userId);
        }
        await db.SaveChangesAsync();
        await realtime.TableChanged(order.TableId);
        return await GetTableAsync(order.TableId);
    }

    /// <summary>Aktif kalem varsa ve kalan 0'sa adisyonu kapatır. Kaydetmez.</summary>
    public void CloseIfSettled(Order order, int userId)
    {
        if (order.Items.Any(i => i.VoidedAt == null) && OrderMath.Remaining(order) == 0)
        {
            order.Status = OrderStatus.Closed;
            order.ClosedAt = Now;
            order.ClosedById = userId;
        }
    }

    private async Task<Order> OpenOrderAsync(int tableId, int userId)
    {
        var order = new Order { TableId = tableId, OpenedAt = Now, OpenedById = userId };
        db.Orders.Add(order);
        try
        {
            await db.SaveChangesAsync();
            return order;
        }
        catch (DbUpdateException)
        {
            // Başka bir cihaz aynı anda bu masaya adisyon açtı (benzersiz indeks); onunkini kullan.
            db.Entry(order).State = EntityState.Detached;
            var existing = await LoadOpenOrderAsync(tableId);
            if (existing is null) throw;
            return existing;
        }
    }

    private async Task<(Order, OrderItem)> LoadEditableItemAsync(int itemId)
    {
        var orderId = await db.OrderItems.Where(i => i.Id == itemId).Select(i => (int?)i.OrderId).FirstOrDefaultAsync()
            ?? throw new DomainException("kalem_yok", "Kalem bulunamadı.", StatusCodes.Status404NotFound);
        var order = await db.Orders.Include(o => o.Items).Include(o => o.Payments).FirstAsync(o => o.Id == orderId);
        if (order.Status != OrderStatus.Open)
            throw new DomainException("hesap_kapali", "Bu hesap kapanmış.", StatusCodes.Status409Conflict);
        var item = order.Items.Single(i => i.Id == itemId);
        if (item.VoidedAt is not null)
            throw new DomainException("kalem_iptal", "Bu kalem zaten iptal edilmiş.", StatusCodes.Status409Conflict);
        if (item.PaymentId is not null)
            throw new DomainException("kalem_odendi", "Ödemesi alınmış kalem değiştirilemez.", StatusCodes.Status409Conflict);
        return (order, item);
    }

    private static void EnsureNotBelowPaid(Order order)
    {
        if (OrderMath.Total(order) < OrderMath.Paid(order))
            throw new DomainException("odenenden_az",
                "Bu hesaptan ödeme alındı; toplam, ödenen tutarın altına düşemez.", StatusCodes.Status409Conflict);
    }

    private async Task<OrderDto> ToDtoAsync(Order o)
    {
        var userIds = o.Items.Select(i => i.AddedById).Concat(o.Payments.Select(p => p.UserId)).Distinct().ToList();
        var names = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name);
        string Name(int id) => names.GetValueOrDefault(id, "?");

        return new OrderDto(o.Id, o.Status, o.OpenedAt, OrderMath.Total(o), OrderMath.Paid(o), OrderMath.Remaining(o),
            o.Items.Where(i => i.VoidedAt == null).OrderBy(i => i.AddedAt).ThenBy(i => i.Id)
                .Select(i => new OrderItemDto(i.Id, i.ProductId, i.ProductName, i.UnitType, i.UnitPriceKurus,
                    i.Quantity, i.LineTotalKurus, i.PaymentId != null, i.AddedAt, Name(i.AddedById)))
                .ToList(),
            o.Payments.OrderBy(p => p.PaidAt).ThenBy(p => p.Id)
                .Select(p => new PaymentDto(p.Id, p.AmountKurus, p.Method, p.PaidAt, Name(p.UserId)))
                .ToList());
    }

    private static DomainException TableNotFound() =>
        new("masa_yok", "Masa bulunamadı.", StatusCodes.Status404NotFound);
}
```

- [ ] **Step 5: Uçları yaz**

`server/Firin.Api/Endpoints/OrderEndpoints.cs`:

```csharp
using System.Security.Claims;
using Firin.Api.Auth;
using Firin.Api.Orders;

namespace Firin.Api.Endpoints;

public static class OrderEndpoints
{
    public record AddItemRequest(int ProductId, int Quantity);
    public record SetQuantityRequest(int Quantity);

    public static void MapOrderEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/tables", (OrderService s) => s.ListTablesAsync()).RequireAuthorization();

        api.MapGet("/tables/{id:int}", (int id, OrderService s) => s.GetTableAsync(id)).RequireAuthorization();

        api.MapPost("/tables/{id:int}/items", (int id, AddItemRequest r, OrderService s, ClaimsPrincipal u) =>
            s.AddItemAsync(id, r.ProductId, r.Quantity, u.UserId())).RequireAuthorization();

        api.MapPut("/items/{id:int}", (int id, SetQuantityRequest r, OrderService s, ClaimsPrincipal u) =>
            s.SetQuantityAsync(id, r.Quantity, u.UserId())).RequireAuthorization();

        api.MapDelete("/items/{id:int}", (int id, OrderService s, ClaimsPrincipal u) =>
            s.VoidItemAsync(id, u.UserId())).RequireAuthorization();
    }
}
```

- [ ] **Step 6: Program.cs'e bağla**

Başa `using Firin.Api.Orders;` ekle.

`builder.Services.AddSingleton<IRealtime, SignalRRealtime>();` satırının altına:

```csharp
builder.Services.AddScoped<OrderService>();
```

`api.MapUserAdminEndpoints();` satırının altına:

```csharp
api.MapOrderEndpoints();
```

- [ ] **Step 7: Testleri çalıştır, geçtiğini gör**

Run: `dotnet test server/Firin.Tests`
Expected: PASS (tüm testler; OrderTests 19 test)

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: masaya kalem ekleme, miktar düzeltme ve iptal"
```

### Task 4: Canlı bağlantı ve masalar ekranı

**Files:**
- Create: `web/src/realtime/RealtimeProvider.tsx`, `web/src/api/orders.ts`, `web/src/lib/time.ts`, `web/src/lib/useNow.ts`, `web/src/pages/TablesPage.tsx`
- Modify: `web/src/components/Shell.tsx`, `web/src/styles.css`
- Delete: `web/src/pages/HomePage.tsx`
- Test: `web/src/lib/time.test.ts`

**Interfaces:**
- Consumes: hub `/hubs/adisyon` olayları (Task 2); `/api/tables`, `/api/catalog` (Task 3, Parça 2)
- Produces:
  - `RealtimeProvider`, `useConnection() → 'connecting' | 'connected' | 'reconnecting'`, `useOnline() → boolean`
  - `api/orders.ts`: tipler `OrderStatus`, `PaymentMethod`, `OrderItem`, `PaymentRow`, `Order`, `TableDetail`, `TableSummary`; `ordersApi.tables()`, `.table(id)`, `.addItem(tableId, productId, quantity)`, `.setQuantity(itemId, quantity)`, `.voidItem(itemId)`, `.catalog()`
  - `lib/time.ts`: `elapsedLabel(fromIso: string, now: Date) → string`; `lib/useNow.ts`: `useNow(intervalMs = 30000) → Date`
  - Query key'ler: `['tables']`, `['table', id]`, `['catalog']`
  - Rota: `/` → `TablesPage`. Kartlar `/masa/:id` adresine gider (Task 5).

- [ ] **Step 1: Başarısız testi yaz**

`web/src/lib/time.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { elapsedLabel } from './time';

const opened = '2026-10-21T06:00:00Z';
const at = (minutes: number, seconds = 0) => new Date(Date.parse(opened) + minutes * 60_000 + seconds * 1000);

describe('elapsedLabel', () => {
  it.each([
    [at(0), 'şimdi'],
    [at(0, 59), 'şimdi'],
    [at(5), '5 dk'],
    [at(59), '59 dk'],
    [at(60), '1 sa'],
    [at(65), '1 sa 5 dk'],
    [at(-3), 'şimdi'],
  ])('%s → %s', (now, expected) => {
    expect(elapsedLabel(opened, now)).toBe(expected);
  });
});
```

Run: `cd web && npm test`
Expected: FAIL, `./time` bulunamadı.

- [ ] **Step 2: Zaman yardımcılarını yaz**

`web/src/lib/time.ts`:

```ts
/** Masanın açık kalma süresi: "şimdi", "12 dk", "1 sa 5 dk". */
export function elapsedLabel(fromIso: string, now: Date): string {
  const minutes = Math.max(0, Math.floor((now.getTime() - Date.parse(fromIso)) / 60_000));
  if (minutes < 1) return 'şimdi';
  if (minutes < 60) return `${minutes} dk`;
  const h = Math.floor(minutes / 60);
  const m = minutes % 60;
  return m ? `${h} sa ${m} dk` : `${h} sa`;
}
```

`web/src/lib/useNow.ts`:

```ts
import { useEffect, useState } from 'react';

export function useNow(intervalMs = 30_000): Date {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const id = window.setInterval(() => setNow(new Date()), intervalMs);
    return () => window.clearInterval(id);
  }, [intervalMs]);
  return now;
}
```

Run: `npm test`
Expected: PASS

- [ ] **Step 3: Adisyon API katmanını yaz**

`web/src/api/orders.ts`:

```ts
import { api } from './client';
import type { CatalogCategory, UnitType } from './types';

export type OrderStatus = 'Open' | 'Closed' | 'Cancelled';
export type PaymentMethod = 'Cash' | 'Card';

export interface OrderItem {
  id: number;
  productId: number;
  productName: string;
  unitType: UnitType;
  unitPriceKurus: number;
  /** Adet ya da gram. */
  quantity: number;
  lineTotalKurus: number;
  isPaid: boolean;
  addedAt: string;
  addedBy: string;
}

export interface PaymentRow {
  id: number;
  amountKurus: number;
  method: PaymentMethod;
  paidAt: string;
  userName: string;
}

export interface Order {
  id: number;
  status: OrderStatus;
  openedAt: string;
  totalKurus: number;
  paidKurus: number;
  remainingKurus: number;
  items: OrderItem[];
  payments: PaymentRow[];
}

export interface TableDetail {
  id: number;
  name: string;
  /** Açık adisyon; masa boşsa null. */
  order: Order | null;
}

export interface TableSummary {
  id: number;
  name: string;
  orderId: number | null;
  totalKurus: number;
  remainingKurus: number;
  openedAt: string | null;
  itemCount: number;
}

export const ordersApi = {
  tables: () => api<TableSummary[]>('/tables'),
  table: (id: number) => api<TableDetail>(`/tables/${id}`),
  addItem: (tableId: number, productId: number, quantity: number) =>
    api<TableDetail>(`/tables/${tableId}/items`, { method: 'POST', json: { productId, quantity } }),
  setQuantity: (itemId: number, quantity: number) =>
    api<TableDetail>(`/items/${itemId}`, { method: 'PUT', json: { quantity } }),
  voidItem: (itemId: number) => api<TableDetail>(`/items/${itemId}`, { method: 'DELETE' }),
  catalog: () => api<CatalogCategory[]>('/catalog'),
};
```

- [ ] **Step 4: Canlı bağlantı sağlayıcısını yaz**

`web/src/realtime/RealtimeProvider.tsx`:

```tsx
import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';
import { createContext, useContext, useEffect, useState, type ReactNode } from 'react';

type Status = 'connecting' | 'connected' | 'reconnecting';

const RealtimeContext = createContext<Status>('connecting');
export const useConnection = () => useContext(RealtimeContext);
export const useOnline = () => useConnection() === 'connected';

/**
 * Hub'a bağlanır, olay gelince ilgili sorguları geçersiz sayar.
 * Bağlantı koparsa sonsuza kadar yeniden dener; tablet uykudan uyanınca hemen dener.
 */
export function RealtimeProvider({ children }: { children: ReactNode }) {
  const qc = useQueryClient();
  const [status, setStatus] = useState<Status>('connecting');

  useEffect(() => {
    let stopped = false;
    let retryTimer: number | undefined;

    const conn = new HubConnectionBuilder()
      .withUrl('/hubs/adisyon')
      .withAutomaticReconnect({ nextRetryDelayInMilliseconds: (c) => Math.min(1000 * 2 ** c.previousRetryCount, 5000) })
      .configureLogging(LogLevel.Warning)
      .build();

    conn.on('TableChanged', (tableId: number) => {
      qc.invalidateQueries({ queryKey: ['tables'] });
      qc.invalidateQueries({ queryKey: ['table', tableId] });
    });
    conn.on('TablesChanged', () => {
      qc.invalidateQueries({ queryKey: ['tables'] });
      qc.invalidateQueries({ queryKey: ['admin', 'tables'] });
    });
    conn.on('CatalogChanged', () => {
      qc.invalidateQueries({ queryKey: ['catalog'] });
      qc.invalidateQueries({ queryKey: ['admin', 'products'] });
      qc.invalidateQueries({ queryKey: ['admin', 'categories'] });
    });

    const schedule = () => {
      window.clearTimeout(retryTimer);
      retryTimer = window.setTimeout(start, 3000);
    };
    const start = async () => {
      if (stopped || conn.state !== HubConnectionState.Disconnected) return;
      try {
        await conn.start();
        if (stopped) return;
        setStatus('connected');
        qc.invalidateQueries(); // kopukken kaçan değişiklikleri al
      } catch {
        if (!stopped) {
          setStatus('reconnecting');
          schedule();
        }
      }
    };

    conn.onreconnecting(() => setStatus('reconnecting'));
    conn.onreconnected(() => {
      setStatus('connected');
      qc.invalidateQueries();
    });
    conn.onclose(() => {
      if (stopped) return;
      setStatus('reconnecting');
      schedule();
    });

    const onVisible = () => {
      if (document.visibilityState === 'visible') start();
    };
    document.addEventListener('visibilitychange', onVisible);
    start();

    return () => {
      stopped = true;
      window.clearTimeout(retryTimer);
      document.removeEventListener('visibilitychange', onVisible);
      conn.stop();
    };
  }, [qc]);

  return <RealtimeContext.Provider value={status}>{children}</RealtimeContext.Provider>;
}
```

- [ ] **Step 5: Masalar ekranını yaz**

`web/src/pages/TablesPage.tsx`:

```tsx
import { useQuery } from '@tanstack/react-query';
import { Link, useNavigate } from 'react-router';
import { ordersApi } from '../api/orders';
import { useMe } from '../auth/useMe';
import { formatKurus } from '../lib/format';
import { elapsedLabel } from '../lib/time';
import { useNow } from '../lib/useNow';

export function TablesPage() {
  const tables = useQuery({ queryKey: ['tables'], queryFn: ordersApi.tables });
  const me = useMe().data;
  const now = useNow();
  const navigate = useNavigate();

  if (tables.isPending) return <p>Yükleniyor…</p>;
  if (tables.isError) return <p className="error">{tables.error.message}</p>;

  if (tables.data.length === 0)
    return (
      <div className="page">
        <h1>Masalar</h1>
        <p>
          Henüz masa yok.{' '}
          {me?.role === 'Owner' ? <Link to="/yonetim/masalar">Masa ekleyin</Link> : 'İşletme sahibinin masa eklemesi gerekiyor.'}
        </p>
      </div>
    );

  const busy = tables.data.filter((t) => t.orderId !== null).length;

  return (
    <div className="page">
      <div className="toolbar">
        <h1>Masalar</h1>
        <span className="muted">{busy} dolu · {tables.data.length - busy} boş</span>
      </div>
      <div className="table-grid">
        {tables.data.map((t) => (
          <button key={t.id} className={`table-card${t.orderId ? ' busy' : ''}`} onClick={() => navigate(`/masa/${t.id}`)}>
            <span className="table-name">{t.name}</span>
            {t.orderId ? (
              <>
                <span className="table-total">{formatKurus(t.remainingKurus)}</span>
                <small>{t.itemCount} kalem · {elapsedLabel(t.openedAt!, now)}</small>
                {t.remainingKurus !== t.totalKurus && <small>toplam {formatKurus(t.totalKurus)}</small>}
              </>
            ) : (
              <small className="muted">boş</small>
            )}
          </button>
        ))}
      </div>
    </div>
  );
}
```

- [ ] **Step 6: Shell'i güncelle**

`web/src/components/Shell.tsx` (tamamını değiştir):

```tsx
import { useQueryClient } from '@tanstack/react-query';
import { Navigate, NavLink, Route, Routes } from 'react-router';
import { api } from '../api/client';
import type { Me } from '../api/types';
import { AdminPage } from '../pages/admin/AdminPage';
import { TablesPage } from '../pages/TablesPage';
import { RealtimeProvider, useConnection } from '../realtime/RealtimeProvider';

export function Shell({ me }: { me: Me }) {
  return (
    <RealtimeProvider>
      <ShellLayout me={me} />
    </RealtimeProvider>
  );
}

function ShellLayout({ me }: { me: Me }) {
  const qc = useQueryClient();
  const connection = useConnection();
  const isOwner = me.role === 'Owner';

  const logout = async () => {
    await api('/auth/logout', { method: 'POST' }).catch(() => undefined);
    qc.removeQueries({ predicate: (q) => q.queryKey[0] !== 'me' });
    qc.setQueryData(['me'], null);
  };

  return (
    <>
      <header className="shell-header">
        <nav>
          <NavLink to="/" end>Masalar</NavLink>
          {isOwner && <NavLink to="/yonetim">Yönetim</NavLink>}
        </nav>
        <div className="shell-user">
          <span>{me.name}</span>
          <button onClick={logout}>Kullanıcı değiştir</button>
        </div>
      </header>
      {connection === 'reconnecting' && (
        <div className="banner" role="status">Kasa bilgisayarına bağlantı yok. Yeniden bağlanılıyor…</div>
      )}
      <main>
        <Routes>
          <Route path="/" element={<TablesPage />} />
          {isOwner && <Route path="/yonetim/*" element={<AdminPage />} />}
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </main>
    </>
  );
}
```

`web/src/pages/HomePage.tsx` dosyasını sil.

- [ ] **Step 7: CSS ekle**

`web/src/styles.css` sonuna ekle:

```css
.muted { color: var(--muted); }
.banner {
  position: sticky; top: 0; z-index: 20;
  padding: 10px 16px; background: var(--danger); color: #fff; text-align: center; font-weight: 600;
}

.table-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(150px, 1fr)); gap: 12px; }
.table-card {
  display: flex; flex-direction: column; align-items: flex-start; justify-content: flex-start; gap: 4px;
  min-height: 112px; padding: 12px; text-align: left;
}
.table-card.busy { background: var(--busy); border-color: var(--accent); }
.table-name { font-weight: 700; font-size: 1.1rem; }
.table-total { font-size: 1.35rem; font-weight: 700; color: var(--accent); }
```

- [ ] **Step 8: Derle ve dene**

```bash
cd "C:/Users/Ali G/Desktop/firin-adisyon/web"
npm test
npm run build
cd ..
dotnet run --project server/Firin.Api
```

Sunucu arka planda çalışırken iki tarayıcı penceresinde `http://localhost:5080` aç:
1. Ana sayfada masa kartları görünür ve hepsi "boş" yazar.
2. İkinci pencerede Yönetim → Masalar'dan "Bahçe 1" eklenir. Birinci pencerede kart sayfa yenilenmeden belirir.
3. Sunucu durdurulur. Birkaç saniye içinde kırmızı "bağlantı yok" bandı çıkar. Sunucu yeniden başlatılınca band kendiliğinden kaybolur.
4. 394 px'te kartlar iki sütun olur, yatay kaydırma olmaz.

Sunucuyu durdur.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: masalar ekranı ve canlı bağlantı"
```

### Task 5: Masa detayı — hesap paneli, ürün seçici, gram tuş takımı

**Files:**
- Create: `web/src/lib/pricing.ts`, `web/src/lib/keypad.ts`, `web/src/pages/table/TableDetailPage.tsx`, `web/src/pages/table/OrderPanel.tsx`, `web/src/pages/table/ProductPicker.tsx`, `web/src/pages/table/GramKeypad.tsx`
- Modify: `web/src/components/Shell.tsx`, `web/src/styles.css`
- Test: `web/src/lib/pricing.test.ts`, `web/src/lib/keypad.test.ts`

**Interfaces:**
- Consumes: `ordersApi`, `TableDetail`, `OrderItem` (Task 4); `useOnline()` (Task 4); `Modal`, `Thumb`, `formatKurus`, `formatPrice`, `formatQuantity` (Parça 1–2)
- Produces:
  - `lib/pricing.ts`: `lineTotal(unit: UnitType, unitPriceKurus: number, quantity: number) → number` (sunucudaki `Pricing.LineTotal` ile aynı sonuç; yalnızca önizleme içindir)
  - `lib/keypad.ts`: `pressKey(current: string, key: string, maxDigits = 5) → string` (`key`: `'0'–'9'`, `'back'`, `'clear'`)
  - `OrderPanel({ table, disabled, onSetQuantity, onVoid, onEditGram, footer })`. `footer` Parça 4'te "Ödeme al" düğmesiyle doldurulur.
  - `TableDetailPage`: rota `/masa/:id`

- [ ] **Step 1: Başarısız testleri yaz**

`web/src/lib/pricing.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { lineTotal } from './pricing';

describe('lineTotal (sunucu PricingTests ile aynı örnekler)', () => {
  it.each([
    ['Piece', 2500, 3, 7500],
    ['Gram', 60000, 250, 15000],
    ['Gram', 45000, 333, 14985],
    ['Gram', 1234, 125, 154],
    ['Gram', 1000, 500, 500],
    ['Gram', 1001, 500, 501],
    ['Gram', 99999, 99999, 9999800],
  ] as const)('%s %d × %d = %d', (unit, price, qty, expected) => {
    expect(lineTotal(unit, price, qty)).toBe(expected);
  });
});
```

`web/src/lib/keypad.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { pressKey } from './keypad';

describe('pressKey', () => {
  it('rakam ekler', () => {
    expect(pressKey('25', '0')).toBe('250');
  });
  it('baştaki sıfırı almaz', () => {
    expect(pressKey('', '0')).toBe('');
  });
  it('hane sınırını aşmaz', () => {
    expect(pressKey('99999', '1')).toBe('99999');
    expect(pressKey('99', '9', 2)).toBe('99');
  });
  it('siler ve temizler', () => {
    expect(pressKey('250', 'back')).toBe('25');
    expect(pressKey('', 'back')).toBe('');
    expect(pressKey('250', 'clear')).toBe('');
  });
  it('bilinmeyen tuşu yok sayar', () => {
    expect(pressKey('25', 'a')).toBe('25');
  });
});
```

Run: `cd web && npm test`
Expected: FAIL, `./pricing` ve `./keypad` bulunamadı.

- [ ] **Step 2: Saf yardımcıları yaz**

`web/src/lib/pricing.ts`:

```ts
import type { UnitType } from '../api/types';

/** Sunucudaki Pricing.LineTotal'ın aynısı. Tutarı sunucu belirler; bu yalnızca önizleme. */
export function lineTotal(unit: UnitType, unitPriceKurus: number, quantity: number): number {
  return unit === 'Piece' ? unitPriceKurus * quantity : Math.round((quantity * unitPriceKurus) / 1000);
}
```

`web/src/lib/keypad.ts`:

```ts
/** Sayı tuş takımı girişini günceller. Baştaki sıfırı ve hane sınırını aşan rakamı yok sayar. */
export function pressKey(current: string, key: string, maxDigits = 5): string {
  if (key === 'back') return current.slice(0, -1);
  if (key === 'clear') return '';
  if (!/^\d$/.test(key)) return current;
  if (current === '' && key === '0') return current;
  if (current.length >= maxDigits) return current;
  return current + key;
}
```

Run: `npm test`
Expected: PASS

- [ ] **Step 3: Gram tuş takımını yaz**

`web/src/pages/table/GramKeypad.tsx`:

```tsx
import { useEffect, useState } from 'react';
import { Modal } from '../../components/Modal';
import { formatKurus, formatPrice } from '../../lib/format';
import { pressKey } from '../../lib/keypad';
import { lineTotal } from '../../lib/pricing';

interface Props {
  title: string;
  pricePerKg: number;
  /** Düzeltmede mevcut gram. */
  initial?: number;
  busy: boolean;
  error: string | null;
  onSubmit: (grams: number) => void;
  onClose: () => void;
}

const digits = ['1', '2', '3', '4', '5', '6', '7', '8', '9'];
const quick = [250, 500, 1000];

export function GramKeypad({ title, pricePerKg, initial, busy, error, onSubmit, onClose }: Props) {
  const [value, setValue] = useState(initial ? String(initial) : '');
  const grams = Number(value) || 0;
  const valid = grams >= 1 && grams <= 99_999;

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (/^\d$/.test(e.key)) setValue((v) => pressKey(v, e.key));
      else if (e.key === 'Backspace') setValue((v) => pressKey(v, 'back'));
      // Odaktaki düğmede Enter zaten tıklama üretir; çift ekleme olmasın.
      else if (e.key === 'Enter' && !(e.target instanceof HTMLButtonElement) && valid && !busy) onSubmit(grams);
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [valid, busy, grams, onSubmit]);

  return (
    <Modal title={title} onClose={onClose}>
      <div className="gram-display">
        <span className="gram-value">{value || '0'} g</span>
        <span className="muted">{formatPrice('Gram', pricePerKg)}</span>
        <strong className="gram-total">{formatKurus(lineTotal('Gram', pricePerKg, grams))}</strong>
      </div>
      <div className="quick">
        {quick.map((g) => (
          <button key={g} type="button" onClick={() => setValue(String(g))}>{g === 1000 ? '1 kg' : `${g} g`}</button>
        ))}
      </div>
      <div className="pinpad">
        {digits.map((k) => (
          <button key={k} type="button" onClick={() => setValue((v) => pressKey(v, k))}>{k}</button>
        ))}
        <button type="button" onClick={() => setValue('')}>C</button>
        <button type="button" onClick={() => setValue((v) => pressKey(v, '0'))}>0</button>
        <button type="button" onClick={() => setValue((v) => pressKey(v, 'back'))} aria-label="Sil">⌫</button>
      </div>
      {error && <p className="error" role="alert">{error}</p>}
      <button className="primary wide" disabled={!valid || busy} onClick={() => onSubmit(grams)}>
        {initial ? 'Güncelle' : 'Ekle'}
      </button>
    </Modal>
  );
}
```

- [ ] **Step 4: Ürün seçiciyi yaz**

`web/src/pages/table/ProductPicker.tsx`:

```tsx
import { useQuery } from '@tanstack/react-query';
import { useState } from 'react';
import { ordersApi } from '../../api/orders';
import type { Product } from '../../api/types';
import { Thumb } from '../../components/Thumb';
import { formatPrice } from '../../lib/format';

export function ProductPicker({ disabled, onPick }: { disabled: boolean; onPick: (p: Product) => void }) {
  const catalog = useQuery({ queryKey: ['catalog'], queryFn: ordersApi.catalog });
  const [categoryId, setCategoryId] = useState<number | null>(null);

  if (catalog.isPending) return <p>Yükleniyor…</p>;
  if (catalog.isError) return <p className="error">{catalog.error.message}</p>;
  if (catalog.data.length === 0) return <p className="muted">Satışta ürün yok.</p>;

  const active = catalog.data.find((c) => c.id === categoryId) ?? catalog.data[0];

  return (
    <div className="picker">
      <div className="chips">
        {catalog.data.map((c) => (
          <button key={c.id} className={c.id === active.id ? 'chip active' : 'chip'} onClick={() => setCategoryId(c.id)}>
            {c.name}
          </button>
        ))}
      </div>
      <div className="product-grid">
        {active.products.map((p) => (
          <button key={p.id} className="product-card" disabled={disabled} onClick={() => onPick(p)}>
            <Thumb url={p.photoUrl} large />
            <span className="product-name">{p.name}</span>
            <small className="muted">{formatPrice(p.unitType, p.priceKurus)}</small>
          </button>
        ))}
      </div>
    </div>
  );
}
```

- [ ] **Step 5: Hesap panelini yaz**

`web/src/pages/table/OrderPanel.tsx`:

```tsx
import { useEffect, useState, type ReactNode } from 'react';
import type { OrderItem, TableDetail } from '../../api/orders';
import { formatKurus, formatPrice, formatQuantity } from '../../lib/format';

interface Props {
  table: TableDetail;
  disabled: boolean;
  onSetQuantity: (item: OrderItem, quantity: number) => void;
  onVoid: (item: OrderItem) => void;
  onEditGram: (item: OrderItem) => void;
  footer?: ReactNode;
}

export function OrderPanel({ table, disabled, onSetQuantity, onVoid, onEditGram, footer }: Props) {
  const order = table.order;
  // İptal iki dokunuşla olur: önce çöp kutusu, sonra "İptal et?". 3 sn içinde onaylanmazsa geri döner.
  const [confirmVoid, setConfirmVoid] = useState<number | null>(null);
  useEffect(() => {
    if (confirmVoid === null) return;
    const id = window.setTimeout(() => setConfirmVoid(null), 3000);
    return () => window.clearTimeout(id);
  }, [confirmVoid]);

  return (
    <div className="order-panel">
      {!order || order.items.length === 0 ? (
        <p className="muted empty">Henüz ürün eklenmedi. Ürünlere dokunarak ekleyin.</p>
      ) : (
        <ul className="order-lines">
          {order.items.map((i) => (
            <li key={i.id} className={i.isPaid ? 'paid' : ''}>
              <div className="line-main">
                <span className="line-name">{i.productName}</span>
                <small className="muted">
                  {formatQuantity(i.unitType, i.quantity)} × {formatPrice(i.unitType, i.unitPriceKurus)}
                  {i.isPaid && ' · ödendi'}
                </small>
              </div>
              <strong className="line-total">{formatKurus(i.lineTotalKurus)}</strong>
              {!i.isPaid && (
                <div className="line-actions">
                  {i.unitType === 'Piece' ? (
                    <>
                      <button aria-label="Bir azalt" disabled={disabled || i.quantity <= 1} onClick={() => onSetQuantity(i, i.quantity - 1)}>−</button>
                      <span className="qty">{i.quantity}</span>
                      <button aria-label="Bir artır" disabled={disabled} onClick={() => onSetQuantity(i, i.quantity + 1)}>+</button>
                    </>
                  ) : (
                    <button disabled={disabled} onClick={() => onEditGram(i)}>Gramı düzelt</button>
                  )}
                  {confirmVoid === i.id ? (
                    <button className="danger" disabled={disabled} onClick={() => { setConfirmVoid(null); onVoid(i); }}>İptal et?</button>
                  ) : (
                    <button className="icon" aria-label="Kalemi iptal et" disabled={disabled} onClick={() => setConfirmVoid(i.id)}>🗑</button>
                  )}
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
      {order && (
        <div className="order-summary">
          <div><span>Toplam</span><strong>{formatKurus(order.totalKurus)}</strong></div>
          {order.paidKurus > 0 && (
            <>
              <div><span>Ödenen</span><span>{formatKurus(order.paidKurus)}</span></div>
              <div className="remaining"><span>Kalan</span><strong>{formatKurus(order.remainingKurus)}</strong></div>
            </>
          )}
        </div>
      )}
      {footer}
    </div>
  );
}
```

- [ ] **Step 6: Masa detay sayfasını yaz**

`web/src/pages/table/TableDetailPage.tsx`:

```tsx
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState } from 'react';
import { Link, useParams } from 'react-router';
import { ordersApi, type OrderItem, type TableDetail } from '../../api/orders';
import type { Product } from '../../api/types';
import { formatKurus } from '../../lib/format';
import { useOnline } from '../../realtime/RealtimeProvider';
import { GramKeypad } from './GramKeypad';
import { OrderPanel } from './OrderPanel';
import { ProductPicker } from './ProductPicker';

type GramTarget = { product: Product } | { item: OrderItem };

export function TableDetailPage() {
  const tableId = Number(useParams().id);
  const qc = useQueryClient();
  const online = useOnline();
  const table = useQuery({
    queryKey: ['table', tableId],
    queryFn: () => ordersApi.table(tableId),
    enabled: Number.isInteger(tableId),
  });
  const [gram, setGram] = useState<GramTarget | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [tab, setTab] = useState<'order' | 'products'>('products');

  // Her işlem güncel masayı döndürür; bu cihaz beklemeden güncellenir, diğerleri SignalR ile.
  const change = useMutation({
    mutationFn: (run: () => Promise<TableDetail>) => run(),
    onSuccess: (detail) => {
      qc.setQueryData(['table', tableId], detail);
      qc.invalidateQueries({ queryKey: ['tables'] });
      setError(null);
      setGram(null);
    },
    onError: (e) => setError(e.message),
  });

  if (table.isPending) return <p>Yükleniyor…</p>;
  if (table.isError)
    return (
      <div className="page">
        <p className="error">{table.error.message}</p>
        <Link to="/">← Masalara dön</Link>
      </div>
    );

  const t = table.data;
  const disabled = !online;
  const pick = (p: Product) => {
    if (p.unitType === 'Gram') setGram({ product: p });
    else change.mutate(() => ordersApi.addItem(tableId, p.id, 1));
  };

  return (
    <div className="table-detail">
      <div className="detail-head">
        <Link to="/" className="back">← Masalar</Link>
        <h1>{t.name}</h1>
        {t.order && <span className="detail-total">{formatKurus(t.order.remainingKurus)}</span>}
      </div>
      {error && !gram && <p className="error" role="alert">{error}</p>}
      <div className="mobile-tabs">
        <button className={tab === 'order' ? 'active' : ''} onClick={() => setTab('order')}>Hesap ({t.order?.items.length ?? 0})</button>
        <button className={tab === 'products' ? 'active' : ''} onClick={() => setTab('products')}>Ürünler</button>
      </div>
      <div className={`detail-panes show-${tab}`}>
        <section className="pane-order">
          <OrderPanel
            table={t}
            disabled={disabled}
            onSetQuantity={(i, q) => change.mutate(() => ordersApi.setQuantity(i.id, q))}
            onVoid={(i) => change.mutate(() => ordersApi.voidItem(i.id))}
            onEditGram={(i) => { setError(null); setGram({ item: i }); }}
          />
        </section>
        <section className="pane-products">
          <ProductPicker disabled={disabled} onPick={(p) => { setError(null); pick(p); }} />
        </section>
      </div>
      {gram && (
        <GramKeypad
          title={'product' in gram ? gram.product.name : gram.item.productName}
          pricePerKg={'product' in gram ? gram.product.priceKurus : gram.item.unitPriceKurus}
          initial={'item' in gram ? gram.item.quantity : undefined}
          busy={change.isPending || disabled}
          error={error}
          onClose={() => setGram(null)}
          onSubmit={(g) =>
            change.mutate(() =>
              'item' in gram ? ordersApi.setQuantity(gram.item.id, g) : ordersApi.addItem(tableId, gram.product.id, g),
            )
          }
        />
      )}
    </div>
  );
}
```

Ürün kartları istek sürerken kilitlenmez. Garson "Çay"a art arda üç kez dokunabilir; sunucu satırları birleştirir, son durum SignalR ile gelir.

- [ ] **Step 7: Rotayı ve CSS'i ekle**

`web/src/components/Shell.tsx` içinde importlara `import { TableDetailPage } from '../pages/table/TableDetailPage';` ekle ve `<Route path="/" element={<TablesPage />} />` satırının altına:

```tsx
          <Route path="/masa/:id" element={<TableDetailPage />} />
```

`web/src/styles.css` sonuna ekle:

```css
button.wide { width: 100%; margin-top: 12px; }

.detail-head { display: flex; align-items: center; gap: 12px; margin-bottom: 12px; flex-wrap: wrap; }
.detail-head h1 { margin: 0; flex: 1; min-width: 0; }
.detail-head .back { display: inline-flex; align-items: center; min-height: 48px; color: var(--muted); text-decoration: none; }
.detail-total { font-size: 1.35rem; font-weight: 700; color: var(--accent); }

.detail-panes { display: grid; grid-template-columns: minmax(300px, 2fr) 3fr; gap: 16px; align-items: start; }
.pane-order { position: sticky; top: 72px; max-height: calc(100dvh - 90px); overflow: auto; }
.mobile-tabs { display: none; }

.order-panel { background: var(--surface); border: 1px solid var(--border); border-radius: var(--radius); padding: 12px; }
.order-panel .empty { margin: 8px 0; }
.order-lines { list-style: none; margin: 0; padding: 0; }
.order-lines li {
  display: grid; grid-template-columns: 1fr auto; gap: 4px 8px; align-items: center;
  padding: 10px 0; border-bottom: 1px solid var(--border);
}
.order-lines li.paid { opacity: 0.6; }
.line-main { display: flex; flex-direction: column; min-width: 0; }
.line-name { font-weight: 600; }
.line-total { white-space: nowrap; }
.line-actions { grid-column: 1 / -1; display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.line-actions button { min-width: 48px; }
.line-actions .qty { min-width: 32px; text-align: center; font-weight: 600; }
.line-actions .icon, .line-actions .danger { margin-left: auto; }
.order-summary { display: flex; flex-direction: column; gap: 4px; padding-top: 12px; }
.order-summary > div { display: flex; justify-content: space-between; }
.order-summary strong { font-size: 1.15rem; }
.order-summary .remaining strong { color: var(--accent); font-size: 1.35rem; }

.chips { display: flex; gap: 8px; overflow-x: auto; padding-bottom: 8px; margin-bottom: 8px; }
.chip { white-space: nowrap; border-radius: 999px; }
.chip.active { background: var(--accent); border-color: var(--accent); color: var(--accent-text); }
.product-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(140px, 1fr)); gap: 12px; }
.product-card { display: flex; flex-direction: column; align-items: center; gap: 6px; padding: 10px; min-height: 180px; text-align: center; }
.product-name { font-weight: 600; }

.gram-display { display: flex; flex-direction: column; align-items: center; gap: 4px; margin-bottom: 12px; }
.gram-value { font-size: 2rem; font-weight: 700; }
.gram-total { font-size: 1.35rem; color: var(--accent); }
.quick { display: grid; grid-template-columns: repeat(3, 1fr); gap: 8px; max-width: 300px; margin: 0 auto 12px; }

@media (max-width: 760px) {
  .detail-panes { grid-template-columns: 1fr; }
  .pane-order { position: static; max-height: none; }
  .mobile-tabs { display: grid; grid-template-columns: 1fr 1fr; gap: 8px; margin-bottom: 12px; }
  .mobile-tabs button.active { background: var(--accent); border-color: var(--accent); color: var(--accent-text); }
  .detail-panes.show-order .pane-products, .detail-panes.show-products .pane-order { display: none; }
}
```

- [ ] **Step 8: Derle ve uçtan uca dene**

```bash
cd "C:/Users/Ali G/Desktop/firin-adisyon/web"
npm test
npm run build
cd ..
dotnet run --project server/Firin.Api
```

Sunucu arka planda çalışırken Parça 2'de oluşturulan katalogla (Çay 15 ₺ adet, Tuzlu kurabiye 600 ₺/kg) dene. Pencere A sahip, pencere B garson (farklı tarayıcı profili ya da gizli pencere) olarak açılır:
1. B: Masa 1 → Çay'a üç kez dokunulur. Tek satır, "3 adet", ₺45,00 görünür.
2. A: Masalar ekranında Masa 1 kartı yenileme olmadan dolu olur ve ₺45,00 yazar.
3. B: Tuzlu kurabiye → 250 g kısayolu. Önizlemede ₺150,00 görünür → Ekle. Toplam ₺195,00 olur.
4. B: Kurabiye satırında "Gramı düzelt" → C → 300 → Güncelle. Satır ₺180,00, toplam ₺225,00 olur.
5. B: Çay satırında "−" → 2 adet. Çöp kutusuna basılır, "İptal et?" görünür. 3 saniye beklenince geri döner. Tekrar basıp onaylanır. Çay satırı kalkar.
6. B: Kurabiye satırı da iptal edilir. Masa boşalır. A'da kart "boş" olur.
7. A: Masa detayında klavyeden test: Tuzlu kurabiye seçilir, klavyeden `500` yazılıp Enter'a basılır. Tek satır eklenir (çift eklenmez).
8. Pencere 394 px'e daraltılır. "Hesap / Ürünler" sekmeleri görünür ve ürün kartları iki sütun olur. Yatay kaydırma yoktur.
9. Pencere 800 px'e (tablet) getirilir. Hesap ve ürünler yan yana durur.

Sunucuyu durdur.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: masa detayı, ürün ekleme ve gram tuş takımı"
```
