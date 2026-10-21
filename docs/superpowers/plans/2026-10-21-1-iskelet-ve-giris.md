# Fırın Adisyon — Parça 1: İskelet ve PIN Girişi — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Kasa PC'de çalışan ASP.NET Core sunucusu, SQLite veri modeli, PIN ile giriş ve React arayüz kabuğu.

**Architecture:** `server/Firin.Api` Minimal API + EF Core SQLite + cookie auth. `web/` React+Vite, derlemesi doğrudan `server/Firin.Api/wwwroot` içine çıkar ve aynı adresten sunulur. Testler `WebApplicationFactory` ile her test için geçici bir veri klasöründe gerçek SQLite dosyası kullanır.

**Tech Stack:** .NET 10, EF Core 10 Sqlite, xUnit, Microsoft.AspNetCore.Mvc.Testing, Microsoft.Extensions.TimeProvider.Testing; React 19, TypeScript, Vite, TanStack Query v5, react-router v7, Vitest.

**Spec:** `docs/superpowers/specs/2026-10-21-firin-adisyon-design.md`

**Parçalar:** 1 iskelet ve giriş → 2 katalog yönetimi → 3 adisyon akışı → 4 ödeme → 5 raporlar → 6 yedek, servis ve mobil geçiş. Her parça kendi dosyasındadır (`docs/superpowers/plans/2026-10-21-<n>-*.md`).

## Global Constraints

- Hedef `net10.0`, `<Nullable>enable</Nullable>`; Node 24, TypeScript `strict`.
- Para her yerde **kuruş** cinsinden tam sayıdır: C# `long`, TS `number`. Float/decimal saklanmaz.
- Gram satır tutarı: `Math.Round(gram * kgFiyatıKuruş / 1000m, MidpointRounding.AwayFromZero)`.
- Veritabanında zaman UTC `DateTime`. Rapor gün sınırları `Europe/Istanbul` saat dilimiyle hesaplanır. Zaman her zaman DI'daki `TimeProvider` üzerinden alınır (`DateTime.UtcNow` yazılmaz).
- API JSON camelCase, enum'lar string (`"Piece"`, `"Gram"`, `"Owner"`...).
- Hatalar ProblemDetails döner; `title` Türkçe kullanıcı mesajı, `code` uzantısı makine kodu (örn. `pin_hatali`).
- Arayüz metinleri Türkçe ve doğru diakritikli. Dokunma hedefleri en az 48 px. 394 px genişlikte yatay kaydırma yok.
- Sunucu portu `5080`, adres `http://0.0.0.0:5080`.
- Veri klasörü: `Firin:DataDir` ayarı, boşsa `<ContentRoot>/data`. İçinde `firin.db`, `photos/`, `backups/`.
- Commit adımları: kullanıcı yürütme başında toplu onay vermediyse commit atmadan önce sor. Commit mesajı sonuna `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` eklenir.

## Dosya haritası (bu parça)

| Dosya | Sorumluluk |
|---|---|
| `Firin.slnx` | Solution |
| `server/Firin.Api/Program.cs` | Servis kaydı ve pipeline |
| `server/Firin.Api/FirinPaths.cs` | Veri klasörü, db, foto, yedek yolları |
| `server/Firin.Api/DomainException.cs` | İş kuralı hatası + ProblemDetails'e çeviren handler |
| `server/Firin.Api/Domain/Entities.cs` | Tüm entity ve enum'lar |
| `server/Firin.Api/Data/AppDb.cs` | DbContext, dönüşümler, indeksler |
| `server/Firin.Api/Data/Migrations/*` | EF migration'ları (üretilir) |
| `server/Firin.Api/Auth/PinHasher.cs` | PIN hash/doğrulama |
| `server/Firin.Api/Auth/PinLockout.cs` | 5 yanlışta 1 dk kilit |
| `server/Firin.Api/Auth/AuthSetup.cs` | Cookie auth, Owner policy, `UserId()` uzantısı |
| `server/Firin.Api/Endpoints/AuthEndpoints.cs` | `/api/auth/*` |
| `server/Firin.Tests/TestApp.cs` | Test sunucusu, sahte saat, oturum açmış istemci yardımcıları |
| `server/Firin.Tests/*Tests.cs` | Testler |
| `web/` | React uygulaması (Task 4) |

---

### Task 1: Backend iskeleti ve sağlık ucu

**Files:**
- Create: `Firin.slnx`, `server/Firin.Api/*` (template), `server/Firin.Tests/*` (template)
- Create: `server/Firin.Api/FirinPaths.cs`, `server/Firin.Api/DomainException.cs`
- Modify: `server/Firin.Api/Program.cs`, `server/Firin.Api/appsettings.json`, `server/Firin.Api/Properties/launchSettings.json`, `.gitignore`
- Test: `server/Firin.Tests/TestApp.cs`, `server/Firin.Tests/HealthTests.cs`

**Interfaces:**
- Produces: `FirinPaths` (singleton: `DataDir`, `PhotosDir`, `BackupsDir`, `DbPath`, `ConnectionString`); `DomainException(string code, string message, int status = 400)`; `TestApp` (`Client`, `Factory`, `Clock: FakeTimeProvider`, `DataDir`, `NewClient()`, `Dispose()`); `public partial class Program`; `GET /api/health → 200 {"status":"ok"}`.

- [ ] **Step 1: Projeleri oluştur**

```bash
cd "C:/Users/Ali G/Desktop/firin-adisyon"
dotnet new sln -n Firin
dotnet new web -o server/Firin.Api -n Firin.Api
dotnet new xunit -o server/Firin.Tests -n Firin.Tests
dotnet sln Firin.slnx add server/Firin.Api/Firin.Api.csproj server/Firin.Tests/Firin.Tests.csproj
dotnet add server/Firin.Tests reference server/Firin.Api
dotnet add server/Firin.Api package Microsoft.EntityFrameworkCore.Sqlite
dotnet add server/Firin.Api package Microsoft.EntityFrameworkCore.Design
dotnet add server/Firin.Api package Microsoft.Extensions.Hosting.WindowsServices
dotnet add server/Firin.Tests package Microsoft.AspNetCore.Mvc.Testing
dotnet add server/Firin.Tests package Microsoft.Extensions.TimeProvider.Testing
dotnet new tool-manifest
dotnet tool install dotnet-ef
```

`dotnet new sln` .NET 10'da `Firin.slnx` üretir. `.sln` üretirse sonraki komutlarda o adı kullan. Template'in ürettiği `server/Firin.Tests/UnitTest1.cs` dosyasını sil.

`.gitignore` sonuna ekle:

```
server/Firin.Api/wwwroot/
TestResults/
```

- [ ] **Step 2: Test yardımcısını ve başarısız testi yaz**

`server/Firin.Tests/TestApp.cs`:

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;

namespace Firin.Tests;

public sealed class TestApp : IDisposable
{
    public string DataDir { get; } =
        Path.Combine(Path.GetTempPath(), "firin-test-" + Guid.NewGuid().ToString("N"));

    // 2026-10-21 09:00 İstanbul = 06:00 UTC
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 21, 6, 0, 0, TimeSpan.Zero));

    public WebApplicationFactory<Program> Factory { get; }
    public HttpClient Client { get; }

    public TestApp()
    {
        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseEnvironment("Testing");
            b.UseSetting("Firin:DataDir", DataDir);
            b.ConfigureServices(s =>
            {
                s.RemoveAll<TimeProvider>();
                s.AddSingleton<TimeProvider>(Clock);
            });
        });
        Client = Factory.CreateClient();
    }

    public HttpClient NewClient() => Factory.CreateClient();

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(DataDir, recursive: true); } catch (IOException) { }
    }
}
```

`server/Firin.Tests/HealthTests.cs`:

```csharp
using System.Net;

namespace Firin.Tests;

public sealed class HealthTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Health_returns_ok()
    {
        var res = await _app.Client.GetAsync("/api/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Contains("ok", await res.Content.ReadAsStringAsync());
    }

    [Fact]
    public void Data_dir_is_created()
    {
        Assert.True(Directory.Exists(Path.Combine(_app.DataDir, "photos")));
    }
}
```

- [ ] **Step 3: Testi çalıştır, başarısız olduğunu gör**

Run: `dotnet test server/Firin.Tests --filter HealthTests`
Expected: FAIL. Derleme hatası (`Program` erişilemez) ya da `/api/health` için 404.

- [ ] **Step 4: Minimal uygulamayı yaz**

`server/Firin.Api/FirinPaths.cs`:

```csharp
namespace Firin.Api;

public sealed class FirinPaths
{
    public FirinPaths(IConfiguration config, IHostEnvironment env)
    {
        var dir = config["Firin:DataDir"];
        DataDir = string.IsNullOrWhiteSpace(dir) ? Path.Combine(env.ContentRootPath, "data") : dir;
        PhotosDir = Path.Combine(DataDir, "photos");
        BackupsDir = Path.Combine(DataDir, "backups");
        Directory.CreateDirectory(PhotosDir);
        Directory.CreateDirectory(BackupsDir);
    }

    public string DataDir { get; }
    public string PhotosDir { get; }
    public string BackupsDir { get; }
    public string DbPath => Path.Combine(DataDir, "firin.db");
    public string ConnectionString => $"Data Source={DbPath}";
}
```

`server/Firin.Api/DomainException.cs`:

```csharp
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace Firin.Api;

/// <summary>İş kuralı ihlali. Kullanıcıya gösterilecek Türkçe mesaj taşır.</summary>
public sealed class DomainException(string code, string message, int status = StatusCodes.Status400BadRequest)
    : Exception(message)
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

public sealed class DomainExceptionHandler(IProblemDetailsService problems) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext ctx, Exception ex, CancellationToken ct)
    {
        if (ex is not DomainException de) return false;
        ctx.Response.StatusCode = de.Status;
        var details = new ProblemDetails { Status = de.Status, Title = de.Message };
        details.Extensions["code"] = de.Code;
        return await problems.TryWriteAsync(new ProblemDetailsContext { HttpContext = ctx, ProblemDetails = details });
    }
}
```

`server/Firin.Api/Program.cs` (tamamını değiştir):

```csharp
using System.Text.Json.Serialization;
using Firin.Api;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(o => o.ServiceName = "FirinAdisyon");

builder.Services.AddSingleton<FirinPaths>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();
app.Services.GetRequiredService<FirinPaths>(); // klasörleri hemen oluştur

app.UseExceptionHandler();
app.UseStatusCodePages();

var api = app.MapGroup("/api");
api.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();

public partial class Program;
```

`server/Firin.Api/appsettings.json` (tamamını değiştir):

```json
{
  "Logging": { "LogLevel": { "Default": "Information", "Microsoft.AspNetCore": "Warning" } },
  "AllowedHosts": "*",
  "Urls": "http://0.0.0.0:5080",
  "Firin": { "DataDir": "" }
}
```

`server/Firin.Api/Properties/launchSettings.json` (tamamını değiştir):

```json
{
  "profiles": {
    "Firin.Api": {
      "commandName": "Project",
      "launchBrowser": false,
      "applicationUrl": "http://0.0.0.0:5080",
      "environmentVariables": { "ASPNETCORE_ENVIRONMENT": "Development" }
    }
  }
}
```

- [ ] **Step 5: Testleri çalıştır, geçtiğini gör**

Run: `dotnet test server/Firin.Tests --filter HealthTests`
Expected: PASS (2 test)

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: backend iskeleti ve sağlık ucu"
```

### Task 2: Veri modeli ve ilk migration

**Files:**
- Create: `server/Firin.Api/Domain/Entities.cs`, `server/Firin.Api/Data/AppDb.cs`, `server/Firin.Api/Data/Migrations/*` (EF üretir)
- Modify: `server/Firin.Api/Program.cs`
- Test: `server/Firin.Tests/DataModelTests.cs`

**Interfaces:**
- Consumes: `FirinPaths.ConnectionString` (Task 1)
- Produces: enum'lar `Role { Owner, Staff }`, `UnitType { Piece, Gram }`, `OrderStatus { Open, Closed, Cancelled }`, `PaymentMethod { Cash, Card }`; entity'ler `User`, `Category`, `Product`, `DiningTable`, `Order`, `OrderItem`, `Payment`, `Setting` (alanlar aşağıda); `AppDb` DbSet adları `Users, Categories, Products, Tables, Orders, OrderItems, Payments, Settings`. Masa başına tek açık adisyon benzersiz indeksle garanti edilir. Uygulama açılışta `Database.Migrate()` çalıştırır.

- [ ] **Step 1: Başarısız testi yaz**

`server/Firin.Tests/DataModelTests.cs`:

```csharp
using Firin.Api.Data;
using Firin.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Firin.Tests;

public sealed class DataModelTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    private AppDb NewDb() => _app.Factory.Services.CreateScope().ServiceProvider.GetRequiredService<AppDb>();

    [Fact]
    public async Task Product_round_trips_with_enum_and_category()
    {
        await using (var db = NewDb())
        {
            var cat = new Category { Name = "Kuru pasta", SortOrder = 1 };
            db.Categories.Add(cat);
            db.Products.Add(new Product { Category = cat, Name = "Tuzlu kurabiye", UnitType = UnitType.Gram, PriceKurus = 60000 });
            await db.SaveChangesAsync();
        }
        await using (var db = NewDb())
        {
            var p = await db.Products.Include(x => x.Category).SingleAsync();
            Assert.Equal(UnitType.Gram, p.UnitType);
            Assert.Equal(60000, p.PriceKurus);
            Assert.Equal("Kuru pasta", p.Category!.Name);
            Assert.True(p.IsActive);
        }
    }

    [Fact]
    public async Task DateTimes_come_back_as_utc()
    {
        await using (var db = NewDb())
        {
            db.Users.Add(new User { Name = "Ayşe", PinHash = "x", Role = Role.Owner });
            db.Tables.Add(new DiningTable { Name = "Masa 1" });
            await db.SaveChangesAsync();
            db.Orders.Add(new Order { TableId = 1, OpenedById = 1, OpenedAt = new DateTime(2026, 10, 21, 6, 0, 0, DateTimeKind.Utc) });
            await db.SaveChangesAsync();
        }
        await using (var db = NewDb())
        {
            var o = await db.Orders.SingleAsync();
            Assert.Equal(DateTimeKind.Utc, o.OpenedAt.Kind);
            Assert.Equal(OrderStatus.Open, o.Status);
        }
    }

    [Fact]
    public async Task Only_one_open_order_per_table()
    {
        await using var db = NewDb();
        db.Users.Add(new User { Name = "Ayşe", PinHash = "x", Role = Role.Owner });
        db.Tables.Add(new DiningTable { Name = "Masa 1" });
        await db.SaveChangesAsync();
        var now = new DateTime(2026, 10, 21, 6, 0, 0, DateTimeKind.Utc);
        db.Orders.Add(new Order { TableId = 1, OpenedById = 1, OpenedAt = now });
        db.Orders.Add(new Order { TableId = 1, OpenedById = 1, OpenedAt = now, Status = OrderStatus.Closed });
        await db.SaveChangesAsync(); // bir açık + bir kapalı sorun değil
        db.Orders.Add(new Order { TableId = 1, OpenedById = 1, OpenedAt = now });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
```

- [ ] **Step 2: Testi çalıştır, başarısız olduğunu gör**

Run: `dotnet test server/Firin.Tests --filter DataModelTests`
Expected: FAIL. `Firin.Api.Data` / `Firin.Api.Domain` bulunamadığı için derleme hatası.

- [ ] **Step 3: Entity'leri yaz**

`server/Firin.Api/Domain/Entities.cs`:

```csharp
namespace Firin.Api.Domain;

public enum Role { Owner, Staff }
public enum UnitType { Piece, Gram }
public enum OrderStatus { Open, Closed, Cancelled }
public enum PaymentMethod { Cash, Card }

public class User
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string PinHash { get; set; }
    public Role Role { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Category
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public List<Product> Products { get; set; } = [];
}

public class Product
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public required string Name { get; set; }
    public UnitType UnitType { get; set; }
    /// <summary>Adetli üründe adet fiyatı, gramlı üründe kilogram fiyatı (kuruş).</summary>
    public long PriceKurus { get; set; }
    /// <summary>`photos/` altındaki dosya adı.</summary>
    public string? PhotoFile { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public class DiningTable
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Order
{
    public int Id { get; set; }
    public int TableId { get; set; }
    public DiningTable? Table { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Open;
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public int OpenedById { get; set; }
    public int? ClosedById { get; set; }
    public List<OrderItem> Items { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public int ProductId { get; set; }
    // Ürünün eklendiği andaki kopyası: fiyat değişse de hesap değişmez.
    public required string ProductName { get; set; }
    public UnitType UnitType { get; set; }
    public long UnitPriceKurus { get; set; }
    /// <summary>Adet ya da gram.</summary>
    public int Quantity { get; set; }
    public long LineTotalKurus { get; set; }
    public int AddedById { get; set; }
    public DateTime AddedAt { get; set; }
    public DateTime? VoidedAt { get; set; }
    public int? VoidedById { get; set; }
    /// <summary>Ürün seçerek ödemede bu kalemi kapatan ödeme.</summary>
    public int? PaymentId { get; set; }
}

public class Payment
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public long AmountKurus { get; set; }
    public PaymentMethod Method { get; set; }
    public DateTime PaidAt { get; set; }
    public int UserId { get; set; }
}

public class Setting
{
    public required string Key { get; set; }
    public required string Value { get; set; }
}
```

- [ ] **Step 4: DbContext'i yaz**

`server/Firin.Api/Data/AppDb.cs`:

```csharp
using Firin.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Firin.Api.Data;

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<DiningTable> Tables => Set<DiningTable>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<Role>().HaveConversion<string>();
        b.Properties<UnitType>().HaveConversion<string>();
        b.Properties<OrderStatus>().HaveConversion<string>();
        b.Properties<PaymentMethod>().HaveConversion<string>();
        b.Properties<DateTime>().HaveConversion<UtcConverter>();
        b.Properties<DateTime?>().HaveConversion<NullableUtcConverter>();
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Setting>().HasKey(s => s.Key);
        m.Entity<Order>()
            .HasIndex(o => o.TableId)
            .HasFilter("\"Status\" = 'Open'")
            .IsUnique();
        m.Entity<Order>().HasIndex(o => o.ClosedAt);
        m.Entity<Product>().HasOne(p => p.Category).WithMany(c => c.Products).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Order>().HasOne(o => o.Table).WithMany().OnDelete(DeleteBehavior.Restrict);
        m.Entity<OrderItem>().HasOne<Payment>().WithMany().HasForeignKey(i => i.PaymentId).OnDelete(DeleteBehavior.Restrict);
    }

    private sealed class UtcConverter() : ValueConverter<DateTime, DateTime>(
        v => v.ToUniversalTime(), v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue ? v.Value.ToUniversalTime() : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
}
```

- [ ] **Step 5: Program.cs'e DbContext ve migration ekle**

`builder.Services.AddSingleton(TimeProvider.System);` satırının altına:

```csharp
builder.Services.AddDbContext<AppDb>((sp, o) =>
    o.UseSqlite(sp.GetRequiredService<FirinPaths>().ConnectionString));
```

`app.Services.GetRequiredService<FirinPaths>(); // klasörleri hemen oluştur` satırının altına:

```csharp
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDb>().Database.Migrate();
```

Dosyanın başına `using Firin.Api.Data;` ve `using Microsoft.EntityFrameworkCore;` ekle.

- [ ] **Step 6: Migration üret**

Run: `dotnet ef migrations add Initial -p server/Firin.Api -o Data/Migrations`
Expected: `Done.` çıktısı ve `server/Firin.Api/Data/Migrations/` altında `*_Initial.cs`. Bu dosyada `IX_Orders_TableId` için `filter: "\"Status\" = 'Open'"` ve `unique: true` olduğunu kontrol et.

- [ ] **Step 7: Testleri çalıştır, geçtiğini gör**

Run: `dotnet test server/Firin.Tests`
Expected: PASS (5 test)

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: veri modeli ve ilk migration"
```

### Task 3: PIN girişi ve yetki

**Files:**
- Create: `server/Firin.Api/Auth/PinHasher.cs`, `server/Firin.Api/Auth/PinLockout.cs`, `server/Firin.Api/Auth/AuthSetup.cs`, `server/Firin.Api/Endpoints/AuthEndpoints.cs`
- Modify: `server/Firin.Api/Program.cs`, `server/Firin.Tests/TestApp.cs`
- Test: `server/Firin.Tests/AuthTests.cs`

**Interfaces:**
- Consumes: `AppDb`, `User`, `Role` (Task 2); `DomainException` (Task 1)
- Produces:
  - `PinHasher` (singleton): `static bool IsValidFormat(string? pin)`, `string Hash(string pin)`, `bool Verify(string hash, string pin)`
  - `AuthSetup.OwnerPolicy = "Owner"`, `services.AddFirinAuth()`, `AuthSetup.SignInAsync(HttpContext, User)`, `ClaimsPrincipal.UserId() → int`
  - Uçlar: `GET /api/auth/status → AuthStatusDto(bool NeedsSetup, List<UserOptionDto> Users)`, `POST /api/auth/setup (SetupRequest(Name, Pin)) → MeDto`, `POST /api/auth/login (LoginRequest(UserId, Pin)) → MeDto | 401 pin_hatali | 423 kilitli`, `GET /api/auth/me → MeDto(Id, Name, Role)`, `POST /api/auth/logout → 204`
  - Test yardımcıları: `TestApp.Json`, `TestApp.Db(...)`, `TestApp.SeedUser(name, pin, role) → int`, `TestApp.LoginAsync(userId, pin) → HttpClient`, `TestApp.OwnerAsync()`, `TestApp.StaffAsync(name)`, `HttpResponseMessage.ReadAs<T>()`, `HttpResponseMessage.ProblemCode()`

- [ ] **Step 1: Test yardımcılarını genişlet**

`server/Firin.Tests/TestApp.cs` başına şu using'leri ekle:

```csharp
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Firin.Api.Auth;
using Firin.Api.Data;
using Firin.Api.Domain;
```

`TestApp` sınıfına, `NewClient()` metodunun altına ekle:

```csharp
    public static readonly JsonSerializerOptions Json =
        new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    public T Db<T>(Func<AppDb, T> work)
    {
        using var scope = Factory.Services.CreateScope();
        return work(scope.ServiceProvider.GetRequiredService<AppDb>());
    }

    public int SeedUser(string name, string pin, Role role) => Db(db =>
    {
        var hasher = Factory.Services.GetRequiredService<PinHasher>();
        var u = new User { Name = name, PinHash = hasher.Hash(pin), Role = role };
        db.Users.Add(u);
        db.SaveChanges();
        return u.Id;
    });

    public async Task<HttpClient> LoginAsync(int userId, string pin)
    {
        var c = NewClient();
        var res = await c.PostAsJsonAsync("/api/auth/login", new { userId, pin });
        res.EnsureSuccessStatusCode();
        return c;
    }

    public Task<HttpClient> OwnerAsync() => LoginAsync(SeedUser("Sahip", "1234", Role.Owner), "1234");
    public Task<HttpClient> StaffAsync(string name = "Garson") => LoginAsync(SeedUser(name, "5678", Role.Staff), "5678");
```

Dosyanın sonuna ekle:

```csharp
public static class HttpExt
{
    public static async Task<T> ReadAs<T>(this HttpResponseMessage res) =>
        (await res.Content.ReadFromJsonAsync<T>(TestApp.Json))!;

    public static async Task<string?> ProblemCode(this HttpResponseMessage res)
    {
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("code", out var c) ? c.GetString() : null;
    }
}
```

- [ ] **Step 2: Başarısız testleri yaz**

`server/Firin.Tests/AuthTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Firin.Api.Domain;
using static Firin.Api.Endpoints.AuthEndpoints;

namespace Firin.Tests;

public sealed class AuthTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    [Fact]
    public async Task Status_needs_setup_when_no_users()
    {
        var s = await (await _app.Client.GetAsync("/api/auth/status")).ReadAs<AuthStatusDto>();
        Assert.True(s.NeedsSetup);
        Assert.Empty(s.Users);
    }

    [Fact]
    public async Task Setup_creates_owner_and_signs_in()
    {
        var res = await _app.Client.PostAsJsonAsync("/api/auth/setup", new { name = "Ayşe", pin = "1234" });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var me = await (await _app.Client.GetAsync("/api/auth/me")).ReadAs<MeDto>();
        Assert.Equal("Ayşe", me.Name);
        Assert.Equal(Role.Owner, me.Role);

        var s = await (await _app.Client.GetAsync("/api/auth/status")).ReadAs<AuthStatusDto>();
        Assert.False(s.NeedsSetup);
        Assert.Equal("Ayşe", Assert.Single(s.Users).Name);
    }

    [Fact]
    public async Task Setup_twice_is_conflict()
    {
        await _app.Client.PostAsJsonAsync("/api/auth/setup", new { name = "Ayşe", pin = "1234" });
        var res = await _app.NewClient().PostAsJsonAsync("/api/auth/setup", new { name = "Mehmet", pin = "9999" });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("kurulum_yapilmis", await res.ProblemCode());
    }

    [Theory]
    [InlineData("12a4")]
    [InlineData("123")]
    [InlineData("12345")]
    public async Task Setup_rejects_bad_pin(string pin)
    {
        var res = await _app.Client.PostAsJsonAsync("/api/auth/setup", new { name = "Ayşe", pin });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("pin_gecersiz", await res.ProblemCode());
    }

    [Fact]
    public async Task Me_without_login_is_401()
    {
        var res = await _app.Client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Login_with_wrong_pin_is_401()
    {
        var id = _app.SeedUser("Garson", "5678", Role.Staff);
        var res = await _app.Client.PostAsJsonAsync("/api/auth/login", new { userId = id, pin = "0000" });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("pin_hatali", await res.ProblemCode());
    }

    [Fact]
    public async Task Five_wrong_pins_lock_user_for_a_minute()
    {
        var id = _app.SeedUser("Garson", "5678", Role.Staff);
        for (var i = 0; i < 5; i++)
            await _app.Client.PostAsJsonAsync("/api/auth/login", new { userId = id, pin = "0000" });

        var locked = await _app.Client.PostAsJsonAsync("/api/auth/login", new { userId = id, pin = "5678" });
        Assert.Equal((HttpStatusCode)423, locked.StatusCode);
        Assert.Equal("kilitli", await locked.ProblemCode());

        _app.Clock.Advance(TimeSpan.FromSeconds(61));
        var ok = await _app.Client.PostAsJsonAsync("/api/auth/login", new { userId = id, pin = "5678" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
    }

    [Fact]
    public async Task Deactivated_user_session_is_rejected()
    {
        var staff = await _app.StaffAsync();
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync("/api/auth/me")).StatusCode);

        _app.Db(db => { db.Users.Single(u => u.Name == "Garson").IsActive = false; return db.SaveChanges(); });
        Assert.Equal(HttpStatusCode.Unauthorized, (await staff.GetAsync("/api/auth/me")).StatusCode);
    }

    [Fact]
    public async Task Logout_ends_session()
    {
        var owner = await _app.OwnerAsync();
        await owner.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await owner.GetAsync("/api/auth/me")).StatusCode);
    }
}
```

- [ ] **Step 3: Testleri çalıştır, başarısız olduğunu gör**

Run: `dotnet test server/Firin.Tests --filter AuthTests`
Expected: FAIL. `Firin.Api.Auth` / `AuthEndpoints` bulunamadığı için derleme hatası.

- [ ] **Step 4: PinHasher ve PinLockout'u yaz**

`server/Firin.Api/Auth/PinHasher.cs`:

```csharp
using Firin.Api.Domain;
using Microsoft.AspNetCore.Identity;

namespace Firin.Api.Auth;

public sealed class PinHasher
{
    private readonly PasswordHasher<User> _hasher = new();

    public static bool IsValidFormat(string? pin) => pin is { Length: 4 } && pin.All(char.IsAsciiDigit);

    public string Hash(string pin) => _hasher.HashPassword(null!, pin);

    public bool Verify(string hash, string pin) =>
        _hasher.VerifyHashedPassword(null!, hash, pin) != PasswordVerificationResult.Failed;
}
```

`server/Firin.Api/Auth/PinLockout.cs`:

```csharp
using System.Collections.Concurrent;

namespace Firin.Api.Auth;

/// <summary>5 yanlış PIN'den sonra kullanıcıyı 1 dakika kilitler. Bellekte tutulur; yeniden başlatma sıfırlar.</summary>
public sealed class PinLockout(TimeProvider clock)
{
    public const int MaxAttempts = 5;
    public static readonly TimeSpan LockDuration = TimeSpan.FromMinutes(1);

    private sealed record State(int Fails, DateTimeOffset? LockedUntil);
    private readonly ConcurrentDictionary<int, State> _states = new();

    public TimeSpan? RemainingLock(int userId)
    {
        if (!_states.TryGetValue(userId, out var s) || s.LockedUntil is not { } until) return null;
        var left = until - clock.GetUtcNow();
        if (left > TimeSpan.Zero) return left;
        _states.TryRemove(userId, out _);
        return null;
    }

    public void Fail(int userId) => _states.AddOrUpdate(userId,
        _ => new State(1, null),
        (_, s) => s.Fails + 1 >= MaxAttempts
            ? new State(0, clock.GetUtcNow() + LockDuration)
            : new State(s.Fails + 1, null));

    public void Success(int userId) => _states.TryRemove(userId, out _);
}
```

- [ ] **Step 5: Cookie auth kurulumunu yaz**

`server/Firin.Api/Auth/AuthSetup.cs`:

```csharp
using System.Security.Claims;
using Firin.Api.Data;
using Firin.Api.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Firin.Api.Auth;

public static class AuthSetup
{
    public const string OwnerPolicy = "Owner";
    private const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;

    public static IServiceCollection AddFirinAuth(this IServiceCollection services)
    {
        services.AddSingleton<PinHasher>();
        services.AddSingleton<PinLockout>();
        services.AddAuthentication(Scheme).AddCookie(o =>
        {
            o.Cookie.Name = "firin_oturum";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.ExpireTimeSpan = TimeSpan.FromDays(30);
            o.SlidingExpiration = true;
            // API: login sayfasına yönlendirme yerine durum kodu
            o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
            // Pasife alınan ya da rolü değişen kullanıcının oturumu hemen düşer.
            o.Events.OnValidatePrincipal = async c =>
            {
                var id = c.Principal?.UserIdOrNull();
                var db = c.HttpContext.RequestServices.GetRequiredService<AppDb>();
                var user = id is null ? null : await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
                if (user is null || !user.IsActive || user.Role.ToString() != c.Principal!.FindFirstValue(ClaimTypes.Role))
                {
                    c.RejectPrincipal();
                    await c.HttpContext.SignOutAsync(Scheme);
                }
            };
        });
        services.AddAuthorizationBuilder()
            .AddPolicy(OwnerPolicy, p => p.RequireRole(nameof(Role.Owner)));
        return services;
    }

    public static Task SignInAsync(HttpContext ctx, User user)
    {
        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Role, user.Role.ToString()),
        ];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
        return ctx.SignInAsync(Scheme, principal, new AuthenticationProperties { IsPersistent = true });
    }

    public static int? UserIdOrNull(this ClaimsPrincipal p) =>
        int.TryParse(p.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static int UserId(this ClaimsPrincipal p) =>
        p.UserIdOrNull() ?? throw new InvalidOperationException("Oturum yok.");
}
```

- [ ] **Step 6: Auth uçlarını yaz**

`server/Firin.Api/Endpoints/AuthEndpoints.cs`:

```csharp
using System.Security.Claims;
using Firin.Api.Auth;
using Firin.Api.Data;
using Firin.Api.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Firin.Api.Endpoints;

public static class AuthEndpoints
{
    public record UserOptionDto(int Id, string Name);
    public record AuthStatusDto(bool NeedsSetup, List<UserOptionDto> Users);
    public record SetupRequest(string Name, string Pin);
    public record LoginRequest(int UserId, string Pin);
    public record MeDto(int Id, string Name, Role Role);

    public static DomainException PinFormatError() => new("pin_gecersiz", "PIN 4 haneli bir sayı olmalı.");

    public static void MapAuthEndpoints(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/auth");

        g.MapGet("/status", async (AppDb db) =>
        {
            var users = await db.Users.Where(u => u.IsActive).OrderBy(u => u.Name)
                .Select(u => new UserOptionDto(u.Id, u.Name)).ToListAsync();
            return new AuthStatusDto(!await db.Users.AnyAsync(), users);
        });

        g.MapPost("/setup", async (SetupRequest req, AppDb db, PinHasher hasher, HttpContext ctx) =>
        {
            if (await db.Users.AnyAsync())
                throw new DomainException("kurulum_yapilmis", "Kurulum zaten yapılmış.", StatusCodes.Status409Conflict);
            var name = req.Name?.Trim();
            if (string.IsNullOrEmpty(name)) throw new DomainException("ad_gerekli", "Ad boş olamaz.");
            if (!PinHasher.IsValidFormat(req.Pin)) throw PinFormatError();

            var user = new User { Name = name, PinHash = hasher.Hash(req.Pin), Role = Role.Owner };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            await AuthSetup.SignInAsync(ctx, user);
            return new MeDto(user.Id, user.Name, user.Role);
        });

        g.MapPost("/login", async (LoginRequest req, AppDb db, PinHasher hasher, PinLockout lockout, HttpContext ctx) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == req.UserId && u.IsActive)
                ?? throw new DomainException("kullanici_yok", "Kullanıcı bulunamadı.", StatusCodes.Status404NotFound);
            if (lockout.RemainingLock(user.Id) is { } left)
                throw new DomainException("kilitli",
                    $"Çok fazla yanlış deneme. {Math.Ceiling(left.TotalSeconds)} saniye sonra tekrar deneyin.",
                    StatusCodes.Status423Locked);
            if (!PinHasher.IsValidFormat(req.Pin) || !hasher.Verify(user.PinHash, req.Pin))
            {
                lockout.Fail(user.Id);
                throw new DomainException("pin_hatali", "PIN hatalı.", StatusCodes.Status401Unauthorized);
            }
            lockout.Success(user.Id);
            await AuthSetup.SignInAsync(ctx, user);
            return new MeDto(user.Id, user.Name, user.Role);
        });

        g.MapGet("/me", async (ClaimsPrincipal p, AppDb db) =>
            await db.Users.FindAsync(p.UserId()) is { } u
                ? Results.Ok(new MeDto(u.Id, u.Name, u.Role))
                : Results.Unauthorized())
            .RequireAuthorization();

        g.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });
    }
}
```

- [ ] **Step 7: Program.cs'e bağla**

Başa `using Firin.Api.Auth;` ve `using Firin.Api.Endpoints;` ekle.

`builder.Services.AddExceptionHandler<DomainExceptionHandler>();` satırının altına:

```csharp
builder.Services.AddFirinAuth();
```

`app.UseStatusCodePages();` satırının altına:

```csharp
app.UseAuthentication();
app.UseAuthorization();
```

`api.MapGet("/health", ...)` satırının altına:

```csharp
api.MapAuthEndpoints();
```

- [ ] **Step 8: Testleri çalıştır, geçtiğini gör**

Run: `dotnet test server/Firin.Tests`
Expected: PASS (önceki 5 + AuthTests 11 = 16 test)

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: PIN ile giriş, kilitleme ve sahip yetkisi"
```

### Task 4: React kabuğu ve giriş ekranı

**Files:**
- Create: `web/package.json`, `web/tsconfig.json`, `web/vite.config.ts`, `web/index.html`, `web/src/main.tsx`, `web/src/App.tsx`, `web/src/styles.css`, `web/src/lib/format.ts`, `web/src/api/client.ts`, `web/src/api/types.ts`, `web/src/auth/useMe.ts`, `web/src/components/PinPad.tsx`, `web/src/components/Shell.tsx`, `web/src/pages/LoginPage.tsx`, `web/src/pages/HomePage.tsx`
- Modify: `server/Firin.Api/Program.cs`
- Test: `web/src/lib/format.test.ts`, `server/Firin.Tests/HealthTests.cs`

**Interfaces:**
- Consumes: `/api/auth/*` (Task 3)
- Produces (sonraki parçalar bunları kullanır):
  - `lib/format.ts`: `formatKurus(kurus: number): string` ("₺1.234,50"), `parseTlToKurus(input: string): number | null`, `formatQuantity(unitType: UnitType, quantity: number): string` ("3 adet", "250 g", "1,25 kg")
  - `api/client.ts`: `api<T>(path: string, init?: RequestInit & { json?: unknown }): Promise<T>` (path `/api` öneki olmadan, örn. `'/auth/me'`), `class ApiError { status: number; code: string | null; message: string }`
  - `api/types.ts`: `Role`, `UnitType`, `Me`, `UserOption`, `AuthStatus`
  - `auth/useMe.ts`: `useMe()` (queryKey `['me']`, oturum yoksa `null`)
  - `components/Shell.tsx`: `Shell({ me })`; menü ve rotalar burada. Sonraki parçalar buraya rota ve menü bağlantısı ekler.
  - CSS sınıfları: `.center`, `.error`, `.form`, `button.primary`, `button.link`, `.shell-header`, `.page`
  - Sunucu: `wwwroot` statik dosyaları + SPA fallback; bilinmeyen `/api/*` yolu 404 döner.

- [ ] **Step 1: Sunucu için başarısız testi yaz**

`server/Firin.Tests/HealthTests.cs` sınıfına ekle:

```csharp
    [Fact]
    public async Task Unknown_api_route_is_404_not_spa()
    {
        var res = await _app.Client.GetAsync("/api/olmayan-yol");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }
```

Run: `dotnet test server/Firin.Tests --filter HealthTests`
Expected: PASS olabilir (henüz fallback yok, 404 zaten). Bu test fallback eklendikten sonra `/api` yollarının SPA'ya düşmediğini korur. Step 2'den sonra tekrar çalıştırılacak.

- [ ] **Step 2: Sunucuya statik dosya ve SPA fallback ekle**

`server/Firin.Api/Program.cs` içinde `app.UseExceptionHandler();` satırının üstüne:

```csharp
app.UseDefaultFiles();
app.UseStaticFiles();
```

`api.MapAuthEndpoints();` satırının altına:

```csharp
api.MapFallback(() => Results.NotFound());
app.MapFallbackToFile("index.html");
```

Run: `dotnet test server/Firin.Tests`
Expected: PASS (17 test)

- [ ] **Step 3: Web projesini oluştur**

`web/package.json`:

```json
{
  "name": "firin-adisyon-web",
  "private": true,
  "type": "module",
  "scripts": {
    "dev": "vite",
    "build": "tsc && vite build",
    "test": "vitest run"
  }
}
```

```bash
cd "C:/Users/Ali G/Desktop/firin-adisyon/web"
npm install react react-dom react-router @tanstack/react-query @microsoft/signalr
npm install -D vite @vitejs/plugin-react typescript @types/react @types/react-dom vitest
```

`web/tsconfig.json`:

```json
{
  "compilerOptions": {
    "target": "ES2022",
    "lib": ["ES2022", "DOM", "DOM.Iterable"],
    "module": "ESNext",
    "moduleResolution": "bundler",
    "jsx": "react-jsx",
    "strict": true,
    "noUnusedLocals": true,
    "noEmit": true,
    "skipLibCheck": true,
    "isolatedModules": true,
    "types": ["vite/client"]
  },
  "include": ["src", "vite.config.ts"]
}
```

`web/vite.config.ts`:

```ts
import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

const backend = 'http://localhost:5080';

export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    host: true,
    proxy: {
      '/api': backend,
      '/photos': backend,
      '/hubs': { target: backend, ws: true },
    },
  },
  build: { outDir: '../server/Firin.Api/wwwroot', emptyOutDir: true },
});
```

`web/index.html`:

```html
<!doctype html>
<html lang="tr">
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover" />
    <meta name="theme-color" content="#b5651d" />
    <meta name="mobile-web-app-capable" content="yes" />
    <meta name="apple-mobile-web-app-capable" content="yes" />
    <meta name="apple-mobile-web-app-title" content="Adisyon" />
    <title>Fırın Adisyon</title>
  </head>
  <body>
    <div id="root"></div>
    <script type="module" src="/src/main.tsx"></script>
  </body>
</html>
```

- [ ] **Step 4: Biçimlendirme için başarısız testi yaz**

`web/src/lib/format.test.ts`:

```ts
import { describe, expect, it } from 'vitest';
import { formatKurus, formatQuantity, parseTlToKurus } from './format';

describe('formatKurus', () => {
  it('Türk lirası biçiminde yazar', () => {
    expect(formatKurus(123450)).toBe('₺1.234,50');
    expect(formatKurus(0)).toBe('₺0,00');
    expect(formatKurus(5)).toBe('₺0,05');
  });
});

describe('parseTlToKurus', () => {
  it.each([
    ['12,50', 1250],
    ['12.5', 1250],
    ['12', 1200],
    ['1.234,50', 123450],
    [' ₺7,05 ', 705],
    ['0,1', 10],
  ])('%s → %d kuruş', (input, expected) => {
    expect(parseTlToKurus(input)).toBe(expected);
  });

  it.each(['', 'abc', '12,345', '-5', '1.234'])('"%s" geçersiz', (input) => {
    expect(parseTlToKurus(input)).toBeNull();
  });
});

describe('formatQuantity', () => {
  it('adet ve gramı okunur yazar', () => {
    expect(formatQuantity('Piece', 3)).toBe('3 adet');
    expect(formatQuantity('Gram', 250)).toBe('250 g');
    expect(formatQuantity('Gram', 1250)).toBe('1,25 kg');
    expect(formatQuantity('Gram', 1000)).toBe('1 kg');
  });
});
```

Run: `npm test`
Expected: FAIL, `./format` bulunamadı.

- [ ] **Step 5: Biçimlendirme ve API katmanını yaz**

`web/src/api/types.ts`:

```ts
export type Role = 'Owner' | 'Staff';
export type UnitType = 'Piece' | 'Gram';

export interface Me {
  id: number;
  name: string;
  role: Role;
}

export interface UserOption {
  id: number;
  name: string;
}

export interface AuthStatus {
  needsSetup: boolean;
  users: UserOption[];
}
```

`web/src/lib/format.ts`:

```ts
import type { UnitType } from '../api/types';

const tl = new Intl.NumberFormat('tr-TR', { style: 'currency', currency: 'TRY' });
const kg = new Intl.NumberFormat('tr-TR', { maximumFractionDigits: 3 });

export function formatKurus(kurus: number): string {
  return tl.format(kurus / 100);
}

/** "12,50", "12.5", "1.234,50", "₺7" → kuruş. Anlaşılmayan girişte null. */
export function parseTlToKurus(input: string): number | null {
  const s = input.replace(/[\s₺]/g, '');
  if (!s) return null;
  const normalized = s.includes(',') ? s.replace(/\./g, '').replace(',', '.') : s;
  if (!/^\d+(\.\d{1,2})?$/.test(normalized)) return null;
  const [whole, frac = ''] = normalized.split('.');
  return Number(whole) * 100 + Number(frac.padEnd(2, '0'));
}

export function formatQuantity(unitType: UnitType, quantity: number): string {
  if (unitType === 'Piece') return `${quantity} adet`;
  if (quantity < 1000) return `${quantity} g`;
  return `${kg.format(quantity / 1000)} kg`;
}
```

`web/src/api/client.ts`:

```ts
export class ApiError extends Error {
  constructor(
    public status: number,
    public code: string | null,
    message: string,
  ) {
    super(message);
  }
}

const defaultMessages: Record<number, string> = {
  401: 'Oturum kapandı, tekrar giriş yapın.',
  403: 'Bu işlem için yetkiniz yok.',
  404: 'Bulunamadı.',
};

/** `path` `/api` öneki olmadan verilir: api('/auth/me'). */
export async function api<T>(path: string, init: RequestInit & { json?: unknown } = {}): Promise<T> {
  const { json, ...rest } = init;
  const headers = new Headers(rest.headers);
  if (json !== undefined) headers.set('Content-Type', 'application/json');

  let res: Response;
  try {
    res = await fetch(`/api${path}`, {
      ...rest,
      headers,
      body: json !== undefined ? JSON.stringify(json) : rest.body,
      credentials: 'same-origin',
    });
  } catch {
    throw new ApiError(0, 'baglanti_yok', 'Kasa bilgisayarına ulaşılamıyor.');
  }

  if (!res.ok) {
    let code: string | null = null;
    let title: string | null = null;
    try {
      const body = await res.json();
      code = body.code ?? null;
      title = body.title ?? null;
    } catch {
      // gövde yok
    }
    // Yalnızca bizim DomainException'larımızın başlığı Türkçedir.
    const message = code && title ? title : (defaultMessages[res.status] ?? 'Bir hata oluştu.');
    throw new ApiError(res.status, code, message);
  }
  if (res.status === 204) return undefined as T;
  return (await res.json()) as T;
}
```

Run: `npm test`
Expected: PASS (format testleri)

- [ ] **Step 6: Oturum kancası ve PIN tuş takımını yaz**

`web/src/auth/useMe.ts`:

```ts
import { useQuery } from '@tanstack/react-query';
import { api, ApiError } from '../api/client';
import type { Me } from '../api/types';

export function useMe() {
  return useQuery({
    queryKey: ['me'],
    queryFn: async () => {
      try {
        return await api<Me>('/auth/me');
      } catch (e) {
        if (e instanceof ApiError && e.status === 401) return null;
        throw e;
      }
    },
    staleTime: Infinity,
  });
}
```

`web/src/components/PinPad.tsx`:

```tsx
import { useCallback, useEffect, useRef, useState } from 'react';

interface Props {
  onComplete: (pin: string) => void;
  disabled?: boolean;
  error?: string | null;
}

const keys = ['1', '2', '3', '4', '5', '6', '7', '8', '9'];

/** 4 haneli PIN. Dokunmatik tuşlarla ya da klavyeden girilir; 4. hanede onComplete çağrılır. */
export function PinPad({ onComplete, disabled, error }: Props) {
  const pinRef = useRef('');
  const [length, setLength] = useState(0);

  const press = useCallback(
    (digit: string) => {
      if (disabled) return;
      const next = pinRef.current + digit;
      if (next.length >= 4) {
        pinRef.current = '';
        setLength(0);
        onComplete(next.slice(0, 4));
      } else {
        pinRef.current = next;
        setLength(next.length);
      }
    },
    [disabled, onComplete],
  );

  const back = useCallback(() => {
    pinRef.current = pinRef.current.slice(0, -1);
    setLength(pinRef.current.length);
  }, []);

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (/^\d$/.test(e.key)) press(e.key);
      else if (e.key === 'Backspace') back();
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [press, back]);

  return (
    <div className="pin">
      <div className="pin-dots" aria-label={`${length} hane girildi`}>
        {[0, 1, 2, 3].map((i) => (
          <span key={i} className={i < length ? 'filled' : ''} />
        ))}
      </div>
      {error && <p className="error" role="alert">{error}</p>}
      <div className="pinpad">
        {keys.map((k) => (
          <button key={k} type="button" onClick={() => press(k)} disabled={disabled}>
            {k}
          </button>
        ))}
        <button type="button" onClick={back} disabled={disabled} aria-label="Sil">
          ⌫
        </button>
        <button type="button" onClick={() => press('0')} disabled={disabled}>
          0
        </button>
      </div>
    </div>
  );
}
```

- [ ] **Step 7: Giriş ve ilk kurulum ekranını yaz**

`web/src/pages/LoginPage.tsx`:

```tsx
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { useState, type FormEvent } from 'react';
import { api } from '../api/client';
import type { AuthStatus, Me, UserOption } from '../api/types';
import { PinPad } from '../components/PinPad';

export function LoginPage() {
  const status = useQuery({ queryKey: ['auth-status'], queryFn: () => api<AuthStatus>('/auth/status') });

  if (status.isPending) return <div className="center">Yükleniyor…</div>;
  if (status.isError) return <div className="center error">{status.error.message}</div>;
  if (status.data.needsSetup) return <SetupForm />;
  return <UserLogin users={status.data.users} />;
}

function UserLogin({ users }: { users: UserOption[] }) {
  const qc = useQueryClient();
  const [selected, setSelected] = useState<UserOption | null>(null);
  const [error, setError] = useState<string | null>(null);
  const login = useMutation({
    mutationFn: (pin: string) => api<Me>('/auth/login', { method: 'POST', json: { userId: selected!.id, pin } }),
    onSuccess: (me) => qc.setQueryData(['me'], me),
    onError: (e) => setError(e.message),
  });

  return (
    <div className="login">
      <h1>Fırın Adisyon</h1>
      {!selected ? (
        <>
          <p>Kim giriş yapıyor?</p>
          <div className="user-grid">
            {users.map((u) => (
              <button key={u.id} className="user-btn" onClick={() => { setSelected(u); setError(null); }}>
                {u.name}
              </button>
            ))}
          </div>
        </>
      ) : (
        <>
          <p><strong>{selected.name}</strong>, PIN'ini gir</p>
          <PinPad disabled={login.isPending} error={error} onComplete={(pin) => { setError(null); login.mutate(pin); }} />
          <button className="link" onClick={() => setSelected(null)}>Başka kullanıcı</button>
        </>
      )}
    </div>
  );
}

function SetupForm() {
  const qc = useQueryClient();
  const [name, setName] = useState('');
  const [pin, setPin] = useState('');
  const [pin2, setPin2] = useState('');
  const [error, setError] = useState<string | null>(null);
  const setup = useMutation({
    mutationFn: () => api<Me>('/auth/setup', { method: 'POST', json: { name: name.trim(), pin } }),
    onSuccess: (me) => qc.setQueryData(['me'], me),
    onError: (e) => setError(e.message),
  });

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (pin !== pin2) return setError('PIN\'ler aynı değil.');
    setError(null);
    setup.mutate();
  };

  return (
    <div className="login">
      <h1>Fırın Adisyon — İlk kurulum</h1>
      <p>İşletme sahibinin hesabını oluştur. Ürünleri, fiyatları ve raporları yalnızca bu hesap yönetir.</p>
      <form className="form" onSubmit={submit}>
        <label>Adın<input value={name} onChange={(e) => setName(e.target.value)} required autoFocus /></label>
        <label>PIN (4 hane)<input type="password" inputMode="numeric" pattern="\d{4}" maxLength={4} value={pin} onChange={(e) => setPin(e.target.value)} required /></label>
        <label>PIN tekrar<input type="password" inputMode="numeric" pattern="\d{4}" maxLength={4} value={pin2} onChange={(e) => setPin2(e.target.value)} required /></label>
        {error && <p className="error" role="alert">{error}</p>}
        <button className="primary" disabled={setup.isPending}>Hesabı oluştur</button>
      </form>
    </div>
  );
}
```

- [ ] **Step 8: Kabuk, ana sayfa ve giriş noktasını yaz**

`web/src/pages/HomePage.tsx` (Parça 3'te masalar ekranıyla değiştirilir):

```tsx
import type { Me } from '../api/types';

export function HomePage({ me }: { me: Me }) {
  return (
    <div className="page">
      <h1>Hoş geldin, {me.name}</h1>
      <p>Masalar ekranı bir sonraki adımda buraya gelecek.</p>
    </div>
  );
}
```

`web/src/components/Shell.tsx`:

```tsx
import { useQueryClient } from '@tanstack/react-query';
import { Navigate, NavLink, Route, Routes } from 'react-router';
import { api } from '../api/client';
import type { Me } from '../api/types';
import { HomePage } from '../pages/HomePage';

export function Shell({ me }: { me: Me }) {
  const qc = useQueryClient();
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
          {isOwner && null /* Parça 2: Yönetim, Parça 5: Raporlar bağlantıları buraya */}
        </nav>
        <div className="shell-user">
          <span>{me.name}</span>
          <button onClick={logout}>Kullanıcı değiştir</button>
        </div>
      </header>
      <main>
        <Routes>
          <Route path="/" element={<HomePage me={me} />} />
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </main>
    </>
  );
}
```

`web/src/App.tsx`:

```tsx
import { useMe } from './auth/useMe';
import { Shell } from './components/Shell';
import { LoginPage } from './pages/LoginPage';

export function App() {
  const me = useMe();
  if (me.isPending) return <div className="center">Yükleniyor…</div>;
  if (me.isError) return <div className="center error">{me.error.message}</div>;
  if (!me.data) return <LoginPage />;
  return <Shell me={me.data} />;
}
```

`web/src/main.tsx`:

```tsx
import { MutationCache, QueryCache, QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router';
import { ApiError } from './api/client';
import { App } from './App';
import './styles.css';

// Herhangi bir istekte 401 gelirse giriş ekranına dön.
const onError = (e: unknown) => {
  if (e instanceof ApiError && e.status === 401) queryClient.setQueryData(['me'], null);
};

const queryClient = new QueryClient({
  queryCache: new QueryCache({ onError }),
  mutationCache: new MutationCache({ onError }),
  defaultOptions: {
    queries: {
      retry: (count, e) => !(e instanceof ApiError && e.status >= 400 && e.status < 500) && count < 2,
    },
  },
});

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </QueryClientProvider>
  </StrictMode>,
);
```

`web/src/styles.css`:

```css
:root {
  --bg: #faf6f0;
  --surface: #ffffff;
  --text: #2b2118;
  --muted: #7a6a5a;
  --accent: #b5651d;
  --accent-text: #ffffff;
  --border: #e6dccf;
  --danger: #b3261e;
  --ok: #2e7d32;
  --busy: #fff1e0;
  --radius: 12px;
  font-family: system-ui, 'Segoe UI', Roboto, sans-serif;
  color: var(--text);
  background: var(--bg);
}

* { box-sizing: border-box; }
body { margin: 0; background: var(--bg); -webkit-tap-highlight-color: transparent; }
h1 { font-size: 1.5rem; margin: 0 0 8px; }

button {
  font: inherit;
  min-height: 48px;
  padding: 0 16px;
  border-radius: var(--radius);
  border: 1px solid var(--border);
  background: var(--surface);
  color: var(--text);
  cursor: pointer;
  touch-action: manipulation;
  user-select: none;
}
button.primary { background: var(--accent); border-color: var(--accent); color: var(--accent-text); font-weight: 600; }
button.danger { color: var(--danger); border-color: var(--danger); }
button.link { border: none; background: none; text-decoration: underline; color: var(--muted); }
button:disabled { opacity: 0.5; cursor: default; }

input, select {
  font: inherit;
  font-size: 16px; /* iOS'ta odaklanınca yakınlaştırmayı önler */
  min-height: 48px;
  width: 100%;
  padding: 0 12px;
  border-radius: var(--radius);
  border: 1px solid var(--border);
  background: var(--surface);
  color: var(--text);
}

.center { display: grid; place-items: center; min-height: 100dvh; padding: 16px; text-align: center; }
.error { color: var(--danger); }
.page { max-width: 1200px; margin: 0 auto; }

.form { display: flex; flex-direction: column; gap: 12px; text-align: left; }
.form label { display: flex; flex-direction: column; gap: 4px; font-weight: 500; }

.login { max-width: 420px; margin: 0 auto; padding: 32px 16px; text-align: center; display: flex; flex-direction: column; gap: 16px; }
.user-grid { display: grid; grid-template-columns: repeat(auto-fill, minmax(140px, 1fr)); gap: 12px; }
.user-btn { min-height: 72px; font-size: 1.1rem; }

.pin { display: flex; flex-direction: column; gap: 16px; }
.pin-dots { display: flex; gap: 12px; justify-content: center; }
.pin-dots span { width: 16px; height: 16px; border-radius: 50%; border: 2px solid var(--accent); }
.pin-dots span.filled { background: var(--accent); }
.pinpad { display: grid; grid-template-columns: repeat(3, 1fr); gap: 12px; width: 100%; max-width: 300px; margin: 0 auto; }
.pinpad button { min-height: 64px; font-size: 1.5rem; }

.shell-header {
  position: sticky; top: 0; z-index: 10;
  display: flex; flex-wrap: wrap; align-items: center; gap: 8px;
  padding: 8px 16px; padding-top: max(8px, env(safe-area-inset-top));
  background: var(--surface); border-bottom: 1px solid var(--border);
}
.shell-header nav { display: flex; gap: 4px; flex: 1; min-width: 0; overflow-x: auto; }
.shell-header nav a {
  display: inline-flex; align-items: center; min-height: 48px; padding: 0 14px;
  border-radius: var(--radius); color: var(--text); text-decoration: none; white-space: nowrap;
}
.shell-header nav a.active { background: var(--busy); color: var(--accent); font-weight: 600; }
.shell-user { display: flex; align-items: center; gap: 8px; color: var(--muted); }
main { padding: 16px; }
```


- [ ] **Step 9: Derle ve testleri çalıştır**

```bash
cd "C:/Users/Ali G/Desktop/firin-adisyon/web"
npm test
npm run build
```

Expected: testler PASS. `tsc` hatasız biter, `server/Firin.Api/wwwroot/index.html` oluşur.

- [ ] **Step 10: Uçtan uca dene**

```bash
cd "C:/Users/Ali G/Desktop/firin-adisyon"
dotnet run --project server/Firin.Api
```

Sunucu arka planda çalışırken tarayıcıda `http://localhost:5080` aç (Chrome eklentisiyle ya da elle):
1. "İlk kurulum" formu görünür. Ad "Ali", PIN 1234 ve tekrarı girilir. Kabuk açılır ve "Hoş geldin, Ali" yazar.
2. Sayfa yenilenir. Oturum korunur.
3. "Kullanıcı değiştir"e basılır. "Ali" düğmesi görünür. 0000 girilince "PIN hatalı." yazar. 1234 klavyeden yazılınca giriş olur.
4. Pencere 394 px genişliğe getirilir. Yatay kaydırma çubuğu olmadığı ve PIN tuşlarının taşmadığı kontrol edilir.
5. `http://localhost:5080/masa/5` gibi bir yol yenilenince yine uygulama açılır (SPA fallback).

Sunucuyu durdur. Deneme sırasında oluşan `server/Firin.Api/data/` klasörü gitignore'dadır.

- [ ] **Step 11: Commit**

```bash
cd "C:/Users/Ali G/Desktop/firin-adisyon"
git add -A
git commit -m "feat: React kabuğu, ilk kurulum ve PIN giriş ekranı"
```
