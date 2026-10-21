# Fırın Adisyon — Parça 6: Ayarlar, Yedek, Mobil ve Kurulum — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bu parçanın sonunda uygulama fırında kullanılmaya hazır olur:
- İşletme adı ayarlanabilir.
- Tabletler QR kodla bağlanır.
- Her gün otomatik yedek alınır ve 30 gün saklanır; sahip elle yedek alıp indirebilir.
- Telefon ve tablette uygulama yerel bir uygulama gibi davranır.
- Kasa PC'de Windows servisi olarak çalışır.

**Architecture:**
- **Ayarlar:** `Settings` tablosunda anahtar-değer olarak tutulur.
- **Yedek:** `BackupService` (singleton) SQLite'ın çevrimiçi yedek API'siyle (`SqliteConnection.BackupDatabase`) uygulama çalışırken tutarlı kopya alır. `AutoBackupService` (BackgroundService) saatte bir bakar; son yedek 24 saatten eskiyse yenisini alır.
- **Yayın:** `scripts/publish.ps1` web'i derler ve self-contained `win-x64` yayını üretir. `scripts/install-service.ps1` yayını `C:\FirinAdisyon` klasörüne kopyalar, Windows servisini kurar ve güvenlik duvarı kuralını açar.

**Tech Stack:** Önceki parçalara ek: web'de `qrcode.react`.

**Ön koşul:** Parça 1–5 tamamlanmış olmalı.

## Global Constraints

- Hedef `net10.0`, `<Nullable>enable</Nullable>`; Node 24, TypeScript `strict`.
- Zaman her zaman DI'daki `TimeProvider` üzerinden alınır.
- Yedek dosya adı `firin-yyyyMMdd-HHmmss.db` olur (UTC). Yedekler 30 günden eskiyse silinir. Bu adla eşleşmeyen dosyalara dokunulmaz.
- Yedek klasörü ayarı mutlak bir yol olmalıdır (örn. `E:\FirinYedek`). Boş bırakılırsa `data\backups` kullanılır.
- Sunucu portu `5080`. Servis adı `FirinAdisyon`. Kurulum klasörü `C:\FirinAdisyon`, veriler `C:\FirinAdisyon\data`.
- Hatalar `DomainException(code, message, status)`. Arayüz metinleri Türkçe ve doğru diakritikli.
- Mobil kurallar:
  - Hover stilleri yalnızca `@media (hover: hover) and (pointer: fine)` içinde yazılır.
  - Dokunma geri bildirimi `:active` ile verilir.
  - Zoom kapatılmaz (`user-scalable=no` yasak); input font boyutu 16 px olur.
  - `user-select: none` yalnızca kontrollerde kullanılır, `body`'de kullanılmaz.
- Commit adımları: kullanıcı yürütme başında toplu onay vermediyse commit atmadan önce sor. Commit mesajı sonuna `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` eklenir.

## Önceki parçalardan kullanılanlar

- `FirinPaths` (`BackupsDir`, `ConnectionString`), `AppDb.Settings`, `Setting` entity'si, `Validate.Name`, `AuthSetup.OwnerPolicy`, `DomainException`
- Test: `TestApp` (`DataDir`, `Clock`, `OwnerAsync`, `StaffAsync`, `Factory`), `HttpExt`
- Web: `api`, `adminApi`, `AdminPage` (sekmeler), `LoginPage`, `Modal`, `styles.css`, `index.html`

---

### Task 1: Ayar ve yedek API'si

**Files:**
- Create: `server/Firin.Api/Settings/SettingsService.cs`, `server/Firin.Api/Settings/NetworkInfo.cs`, `server/Firin.Api/Backups/BackupService.cs`, `server/Firin.Api/Backups/AutoBackupService.cs`, `server/Firin.Api/Endpoints/SettingsEndpoints.cs`
- Modify: `server/Firin.Api/Program.cs`, `server/Firin.Api/appsettings.json`, `server/Firin.Tests/TestApp.cs`
- Test: `server/Firin.Tests/SettingsBackupTests.cs`

**Interfaces:**
- Produces:
  - `SettingsService` (scoped): `BusinessNameKey = "BusinessName"`, `BackupDirKey = "BackupDir"`, `GetAsync(string key) → Task<string?>`, `SetAsync(string key, string? value)` (boş değer anahtarı siler)
  - `BackupService` (singleton): `CreateAsync() → Task<BackupDto>`, `List(string dir) → List<BackupDto>` (yeniden eskiye), `ResolveDirAsync() → Task<string>`, `PathOfAsync(string name) → Task<string?>`, `LastBackupAt`, `LastError`; `KeepDays = 30`
  - `BackupDto(string FileName, long SizeBytes, DateTime CreatedAt)`
  - `NetworkInfo.LanUrls(int port) → List<string>`
  - Uçlar: `GET /api/info → InfoDto(string BusinessName)` (oturumsuz); `GET|PUT /api/admin/settings` (`SettingsInput(string? BusinessName, string? BackupDir)` `→ SettingsDto(string BusinessName, string? BackupDir, string EffectiveBackupDir, DateTime? LastBackupAt, string? LastBackupError, List<string> LanUrls)`); `GET|POST /api/admin/backups`; `GET /api/admin/backups/{name}` (dosya indirme)
  - Ayar: `Firin:AutoBackup` (varsayılan `true`)
  - Hata kodları: `yol_gecersiz`, `klasor_yazilamiyor`, `yedek_hatasi` (500)

- [ ] **Step 1: Testlerde otomatik yedeği kapat**

`server/Firin.Tests/TestApp.cs` içinde `b.UseSetting("Firin:DataDir", DataDir);` satırının altına ekle:

```csharp
            b.UseSetting("Firin:AutoBackup", "false");
```

- [ ] **Step 2: Başarısız testleri yaz**

`server/Firin.Tests/SettingsBackupTests.cs`:

```csharp
using System.Net;
using System.Net.Http.Json;
using Firin.Api.Backups;
using Firin.Api.Endpoints;
using Microsoft.Data.Sqlite;
using static Firin.Api.Endpoints.SettingsEndpoints;

namespace Firin.Tests;

public sealed class SettingsBackupTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    private string DefaultBackupDir => Path.Combine(_app.DataDir, "backups");

    [Fact]
    public async Task Info_is_public_and_shows_business_name()
    {
        var info = await (await _app.Client.GetAsync("/api/info")).ReadAs<InfoDto>();
        Assert.Equal("Fırın Adisyon", info.BusinessName);

        var owner = await _app.OwnerAsync();
        var res = await owner.PutAsJsonAsync("/api/admin/settings", new { businessName = "Ekmek Teknesi", backupDir = (string?)null });
        Assert.Equal("Ekmek Teknesi", (await res.ReadAs<SettingsDto>()).BusinessName);
        Assert.Equal("Ekmek Teknesi", (await (await _app.Client.GetAsync("/api/info")).ReadAs<InfoDto>()).BusinessName);
    }

    [Fact]
    public async Task Staff_cannot_touch_settings_or_backups()
    {
        var staff = await _app.StaffAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsync("/api/admin/backups", null)).StatusCode);
    }

    [Fact]
    public async Task Backup_is_a_readable_copy_of_the_database()
    {
        var owner = await _app.OwnerAsync();
        var res = await owner.PostAsync("/api/admin/backups", null);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var b = await res.ReadAs<BackupDto>();
        Assert.Equal("firin-20261021-060000.db", b.FileName);

        var path = Path.Combine(DefaultBackupDir, b.FileName);
        await using var conn = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM Users";
        Assert.Equal(1L, await cmd.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Old_backups_are_pruned_and_other_files_kept()
    {
        var owner = await _app.OwnerAsync();
        Directory.CreateDirectory(DefaultBackupDir);
        var old = Path.Combine(DefaultBackupDir, "firin-20260901-120000.db");
        var recent = Path.Combine(DefaultBackupDir, "firin-20261001-120000.db");
        var other = Path.Combine(DefaultBackupDir, "notlar.txt");
        foreach (var f in new[] { old, recent, other }) await File.WriteAllTextAsync(f, "x");

        await owner.PostAsync("/api/admin/backups", null);

        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
        Assert.True(File.Exists(other));

        var list = await (await owner.GetAsync("/api/admin/backups")).ReadAs<List<BackupDto>>();
        Assert.Equal(new[] { "firin-20261021-060000.db", "firin-20261001-120000.db" }, list.Select(x => x.FileName).ToArray());
    }

    [Fact]
    public async Task Custom_backup_dir_is_used()
    {
        var owner = await _app.OwnerAsync();
        var usb = Path.Combine(_app.DataDir, "usb");
        var res = await owner.PutAsJsonAsync("/api/admin/settings", new { businessName = (string?)null, backupDir = usb });
        Assert.Equal(usb, (await res.ReadAs<SettingsDto>()).EffectiveBackupDir);

        var b = await (await owner.PostAsync("/api/admin/backups", null)).ReadAs<BackupDto>();
        Assert.True(File.Exists(Path.Combine(usb, b.FileName)));
    }

    [Fact]
    public async Task Relative_backup_dir_is_rejected()
    {
        var owner = await _app.OwnerAsync();
        var res = await owner.PutAsJsonAsync("/api/admin/settings", new { businessName = (string?)null, backupDir = "yedekler" });
        Assert.Equal("yol_gecersiz", await res.ProblemCode());
    }

    [Fact]
    public async Task Backup_can_be_downloaded_by_name_only()
    {
        var owner = await _app.OwnerAsync();
        var b = await (await owner.PostAsync("/api/admin/backups", null)).ReadAs<BackupDto>();

        var file = await owner.GetAsync($"/api/admin/backups/{b.FileName}");
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal(b.SizeBytes, (await file.Content.ReadAsByteArrayAsync()).Length);

        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/api/admin/backups/firin.db")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/api/admin/backups/firin-20200101-000000.db")).StatusCode);
    }

    [Fact]
    public async Task Settings_report_last_backup_and_lan_urls()
    {
        var owner = await _app.OwnerAsync();
        await owner.PostAsync("/api/admin/backups", null);
        var s = await (await owner.GetAsync("/api/admin/settings")).ReadAs<SettingsDto>();
        Assert.Equal(_app.Clock.GetUtcNow().UtcDateTime, s.LastBackupAt!.Value);
        Assert.Null(s.LastBackupError);
        Assert.All(s.LanUrls, u => Assert.EndsWith(":5080", u));
    }
}
```

- [ ] **Step 3: Testleri çalıştır, başarısız olduğunu gör**

Run: `dotnet test server/Firin.Tests --filter SettingsBackupTests`
Expected: FAIL. `Firin.Api.Backups` / `SettingsEndpoints` bulunamadığı için derleme hatası.

- [ ] **Step 4: Ayar servisini ve ağ bilgisini yaz**

`server/Firin.Api/Settings/SettingsService.cs`:

```csharp
using Firin.Api.Data;
using Firin.Api.Domain;

namespace Firin.Api.Settings;

public sealed class SettingsService(AppDb db)
{
    public const string BusinessNameKey = "BusinessName";
    public const string BackupDirKey = "BackupDir";

    public async Task<string?> GetAsync(string key) => (await db.Settings.FindAsync(key))?.Value;

    /// <summary>Boş değer anahtarı siler (varsayılana döner).</summary>
    public async Task SetAsync(string key, string? value)
    {
        var s = await db.Settings.FindAsync(key);
        if (string.IsNullOrWhiteSpace(value))
        {
            if (s is not null) db.Settings.Remove(s);
        }
        else if (s is null)
        {
            db.Settings.Add(new Setting { Key = key, Value = value.Trim() });
        }
        else
        {
            s.Value = value.Trim();
        }
        await db.SaveChangesAsync();
    }
}
```

`server/Firin.Api/Settings/NetworkInfo.cs`:

```csharp
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Firin.Api.Settings;

public static class NetworkInfo
{
    /// <summary>Tabletlerin bağlanabileceği yerel ağ adresleri, örn. http://192.168.1.20:5080</summary>
    public static List<string> LanUrls(int port) => NetworkInterface.GetAllNetworkInterfaces()
        .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
        .SelectMany(n => n.GetIPProperties().UnicastAddresses)
        .Select(a => a.Address)
        .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)
            && !a.ToString().StartsWith("169.254.", StringComparison.Ordinal))
        .Select(a => $"http://{a}:{port}")
        .Distinct()
        .ToList();

    /// <summary>"Urls" ayarındaki ilk adresin portu; okunamazsa 5080.</summary>
    public static int Port(IConfiguration config)
    {
        var first = config["Urls"]?.Split(';')[0].Replace("0.0.0.0", "localhost").Replace("*", "localhost");
        return Uri.TryCreate(first, UriKind.Absolute, out var uri) ? uri.Port : 5080;
    }
}
```

- [ ] **Step 5: Yedek servislerini yaz**

`server/Firin.Api/Backups/BackupService.cs`:

```csharp
using System.Globalization;
using System.Text.RegularExpressions;
using Firin.Api.Settings;
using Microsoft.Data.Sqlite;

namespace Firin.Api.Backups;

public record BackupDto(string FileName, long SizeBytes, DateTime CreatedAt);

public sealed partial class BackupService(FirinPaths paths, IServiceScopeFactory scopes, TimeProvider clock, ILogger<BackupService> log)
{
    public const int KeepDays = 30;
    private const string StampFormat = "yyyyMMdd-HHmmss";
    private readonly SemaphoreSlim _lock = new(1, 1);

    [GeneratedRegex(@"^firin-(\d{8}-\d{6})\.db$")]
    private static partial Regex NamePattern();

    public DateTime? LastBackupAt { get; private set; }
    public string? LastError { get; private set; }

    public async Task<string> ResolveDirAsync()
    {
        using var scope = scopes.CreateScope();
        var custom = await scope.ServiceProvider.GetRequiredService<SettingsService>().GetAsync(SettingsService.BackupDirKey);
        return string.IsNullOrWhiteSpace(custom) ? paths.BackupsDir : custom;
    }

    public async Task<BackupDto> CreateAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var dir = await ResolveDirAsync();
            Directory.CreateDirectory(dir);
            var now = clock.GetUtcNow().UtcDateTime;
            var name = $"firin-{now.ToString(StampFormat, CultureInfo.InvariantCulture)}.db";
            var path = Path.Combine(dir, name);

            // Çevrimiçi yedek: uygulama çalışırken de tutarlı kopya verir. Pooling kapalı, dosya hemen bırakılır.
            var target = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString();
            await using (var src = new SqliteConnection(paths.ConnectionString))
            await using (var dst = new SqliteConnection(target))
            {
                await src.OpenAsync();
                await dst.OpenAsync();
                src.BackupDatabase(dst);
            }

            Prune(dir, now);
            LastBackupAt = now;
            LastError = null;
            return new BackupDto(name, new FileInfo(path).Length, now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException)
        {
            LastError = ex.Message;
            log.LogError(ex, "Yedek alınamadı");
            throw new DomainException("yedek_hatasi", $"Yedek alınamadı: {ex.Message}", StatusCodes.Status500InternalServerError);
        }
        finally
        {
            _lock.Release();
        }
    }

    public List<BackupDto> List(string dir)
    {
        if (!Directory.Exists(dir)) return [];
        return Directory.EnumerateFiles(dir, "firin-*.db")
            .Select(p => (Path: p, Stamp: StampOf(Path.GetFileName(p))))
            .Where(x => x.Stamp is not null)
            .OrderByDescending(x => x.Stamp)
            .Select(x => new BackupDto(Path.GetFileName(x.Path), new FileInfo(x.Path).Length, x.Stamp!.Value))
            .ToList();
    }

    public async Task<string?> PathOfAsync(string name)
    {
        if (StampOf(name) is null) return null; // yalnızca bizim adlandırdığımız dosyalar; yol geçişi olmaz
        var path = Path.Combine(await ResolveDirAsync(), name);
        return File.Exists(path) ? path : null;
    }

    private void Prune(string dir, DateTime nowUtc)
    {
        foreach (var b in List(dir).Where(b => b.CreatedAt < nowUtc.AddDays(-KeepDays)))
        {
            try { File.Delete(Path.Combine(dir, b.FileName)); }
            catch (IOException ex) { log.LogWarning(ex, "Eski yedek silinemedi: {File}", b.FileName); }
        }
    }

    private static DateTime? StampOf(string fileName)
    {
        var m = NamePattern().Match(fileName);
        return m.Success && DateTime.TryParseExact(m.Groups[1].Value, StampFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)
            ? t
            : null;
    }
}
```

`server/Firin.Api/Backups/AutoBackupService.cs`:

```csharp
namespace Firin.Api.Backups;

/// <summary>Saatte bir bakar; son yedek 24 saatten eskiyse yenisini alır. Bilgisayar açılınca da hemen bakar.</summary>
public sealed class AutoBackupService(BackupService backups, TimeProvider clock, IConfiguration config, ILogger<AutoBackupService> log)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!config.GetValue("Firin:AutoBackup", true)) return;
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), clock);
        do
        {
            try
            {
                var latest = backups.List(await backups.ResolveDirAsync()).FirstOrDefault();
                if (latest is null || clock.GetUtcNow().UtcDateTime - latest.CreatedAt >= TimeSpan.FromHours(24))
                    await backups.CreateAsync();
            }
            catch (Exception ex)
            {
                log.LogError(ex, "Otomatik yedek başarısız");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
```

- [ ] **Step 6: Uçları yaz**

`server/Firin.Api/Endpoints/SettingsEndpoints.cs`:

```csharp
using Firin.Api.Auth;
using Firin.Api.Backups;
using Firin.Api.Settings;

namespace Firin.Api.Endpoints;

public static class SettingsEndpoints
{
    public const string DefaultBusinessName = "Fırın Adisyon";

    public record InfoDto(string BusinessName);
    public record SettingsInput(string? BusinessName, string? BackupDir);
    public record SettingsDto(string BusinessName, string? BackupDir, string EffectiveBackupDir,
        DateTime? LastBackupAt, string? LastBackupError, List<string> LanUrls);

    public static void MapSettingsEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/info", async (SettingsService s) =>
            new InfoDto(await s.GetAsync(SettingsService.BusinessNameKey) ?? DefaultBusinessName));

        var g = api.MapGroup("/admin/settings").RequireAuthorization(AuthSetup.OwnerPolicy);

        g.MapGet("/", (SettingsService s, BackupService b, IConfiguration c) => ToDtoAsync(s, b, c));

        g.MapPut("/", async (SettingsInput input, SettingsService s, BackupService b, IConfiguration c) =>
        {
            var name = string.IsNullOrWhiteSpace(input.BusinessName) ? null : Validate.Name(input.BusinessName);
            var dir = string.IsNullOrWhiteSpace(input.BackupDir) ? null : input.BackupDir.Trim();
            if (dir is not null) EnsureWritable(dir);
            await s.SetAsync(SettingsService.BusinessNameKey, name);
            await s.SetAsync(SettingsService.BackupDirKey, dir);
            return await ToDtoAsync(s, b, c);
        });

        var backups = api.MapGroup("/admin/backups").RequireAuthorization(AuthSetup.OwnerPolicy);

        backups.MapGet("/", async (BackupService b) => b.List(await b.ResolveDirAsync()));
        backups.MapPost("/", (BackupService b) => b.CreateAsync());
        backups.MapGet("/{name}", async (string name, BackupService b) =>
            await b.PathOfAsync(name) is { } path
                ? Results.File(path, "application/octet-stream", name)
                : Results.NotFound());
    }

    private static async Task<SettingsDto> ToDtoAsync(SettingsService s, BackupService b, IConfiguration c) => new(
        await s.GetAsync(SettingsService.BusinessNameKey) ?? DefaultBusinessName,
        await s.GetAsync(SettingsService.BackupDirKey),
        await b.ResolveDirAsync(),
        b.LastBackupAt,
        b.LastError,
        NetworkInfo.LanUrls(NetworkInfo.Port(c)));

    private static void EnsureWritable(string dir)
    {
        if (!Path.IsPathFullyQualified(dir))
            throw new DomainException("yol_gecersiz", @"Yedek klasörü tam yol olmalı, örn. E:\FirinYedek");
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".yazma-testi");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DomainException("klasor_yazilamiyor", $"Bu klasöre yazılamıyor: {ex.Message}");
        }
    }
}
```

- [ ] **Step 7: Program.cs'e bağla**

Başa `using Firin.Api.Backups;` ve `using Firin.Api.Settings;` ekle. `builder.Services.AddScoped<ReportService>();` satırının altına:

```csharp
builder.Services.AddScoped<SettingsService>();
builder.Services.AddSingleton<BackupService>();
builder.Services.AddHostedService<AutoBackupService>();
```

`api.MapReportEndpoints();` satırının altına `api.MapSettingsEndpoints();` ekle.

`server/Firin.Api/appsettings.json` içinde `"Firin"` nesnesini şöyle yap:

```json
  "Firin": { "DataDir": "", "TimeZone": "Europe/Istanbul", "AutoBackup": true }
```

- [ ] **Step 8: Testleri çalıştır, geçtiğini gör**

Run: `dotnet test server/Firin.Tests`
Expected: PASS (tüm testler; SettingsBackupTests 8 test)

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "feat: işletme ayarları, otomatik ve elle yedek"
```

### Task 2: Ayarlar ekranı ve tablet bağlantı QR'ı

**Files:**
- Create: `web/src/pages/admin/SettingsAdmin.tsx`
- Modify: `web/src/api/admin.ts`, `web/src/pages/admin/AdminPage.tsx`, `web/src/pages/LoginPage.tsx`, `web/src/styles.css`

**Interfaces:**
- Consumes: `/api/info`, `/api/admin/settings`, `/api/admin/backups` (Task 1)
- Produces:
  - `adminApi.settings()`, `.saveSettings(input)`, `.backups()`, `.createBackup()`; tipler `SettingsRow`, `BackupRow`
  - Query key'ler: `['info']`, `['admin', 'settings']`, `['admin', 'backups']`
  - Yönetim sekmesi `ayarlar`

- [ ] **Step 1: QR paketini kur**

```bash
cd "C:/Users/Ali G/Desktop/firin-adisyon/web"
npm install qrcode.react
```

- [ ] **Step 2: API'yi genişlet**

`web/src/api/admin.ts` içinde tiplerin altına ekle:

```ts
export interface SettingsRow {
  businessName: string;
  backupDir: string | null;
  effectiveBackupDir: string;
  lastBackupAt: string | null;
  lastBackupError: string | null;
  lanUrls: string[];
}
export interface SettingsInput { businessName: string | null; backupDir: string | null }
export interface BackupRow { fileName: string; sizeBytes: number; createdAt: string }
```

`adminApi` nesnesine ekle:

```ts
  settings: () => api<SettingsRow>('/admin/settings'),
  saveSettings: (i: SettingsInput) => put<SettingsRow>('/admin/settings', i),
  backups: () => api<BackupRow[]>('/admin/backups'),
  createBackup: () => api<BackupRow>('/admin/backups', { method: 'POST' }),
```

- [ ] **Step 3: Ayarlar sekmesini yaz**

`web/src/pages/admin/SettingsAdmin.tsx`:

```tsx
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { QRCodeSVG } from 'qrcode.react';
import { useState } from 'react';
import { adminApi, type SettingsRow } from '../../api/admin';
import { formatDateTime } from '../../lib/dates';

export function SettingsAdmin() {
  const settings = useQuery({ queryKey: ['admin', 'settings'], queryFn: adminApi.settings });
  if (settings.isPending) return <p>Yükleniyor…</p>;
  if (settings.isError) return <p className="error">{settings.error.message}</p>;
  return (
    <div className="settings">
      <SettingsForm initial={settings.data} />
      <Connection urls={settings.data.lanUrls} />
      <Backups settings={settings.data} />
    </div>
  );
}

function SettingsForm({ initial }: { initial: SettingsRow }) {
  const qc = useQueryClient();
  const [name, setName] = useState(initial.businessName);
  const [dir, setDir] = useState(initial.backupDir ?? '');
  const [msg, setMsg] = useState<{ ok: boolean; text: string } | null>(null);
  const save = useMutation({
    mutationFn: () => adminApi.saveSettings({ businessName: name.trim() || null, backupDir: dir.trim() || null }),
    onSuccess: (s) => {
      qc.setQueryData(['admin', 'settings'], s);
      qc.invalidateQueries({ queryKey: ['info'] });
      qc.invalidateQueries({ queryKey: ['admin', 'backups'] });
      setMsg({ ok: true, text: 'Kaydedildi.' });
    },
    onError: (e) => setMsg({ ok: false, text: e.message }),
  });

  return (
    <section>
      <h2>İşletme</h2>
      <form className="form" onSubmit={(e) => { e.preventDefault(); setMsg(null); save.mutate(); }}>
        <label>İşletme adı (giriş ekranında görünür)<input value={name} onChange={(e) => setName(e.target.value)} maxLength={60} /></label>
        <label>
          Yedek klasörü (boş bırakılırsa programın kendi klasörü)
          <input value={dir} onChange={(e) => setDir(e.target.value)} placeholder="E:\FirinYedek" autoCapitalize="none" autoCorrect="off" spellCheck={false} />
        </label>
        <p className="hint">Bilgisayar bozulursa veriler kaybolmasın diye yedek klasörü olarak bir USB bellek ya da harici disk seçin.</p>
        {msg && <p className={msg.ok ? 'ok' : 'error'} role="status">{msg.text}</p>}
        <button className="primary" disabled={save.isPending}>Kaydet</button>
      </form>
    </section>
  );
}

function Connection({ urls }: { urls: string[] }) {
  return (
    <section>
      <h2>Tablet ve telefon bağlantısı</h2>
      {urls.length === 0 ? (
        <p className="error">Bu bilgisayarın ağ adresi bulunamadı. Wi-Fi ya da kablo bağlantısını kontrol edin.</p>
      ) : (
        <>
          <p className="hint">Tablet ya da telefon fırının Wi-Fi ağına bağlıyken kamerayla bu kodu okutun. Açılan sayfayı tarayıcı menüsünden "Ana ekrana ekle" ile uygulama gibi kaydedin.</p>
          <div className="qr-list">
            {urls.map((u) => (
              <figure key={u} className="qr">
                <QRCodeSVG value={u} size={160} marginSize={2} />
                <figcaption>{u}</figcaption>
              </figure>
            ))}
          </div>
          <p className="hint">Adres değişmesin diye modem ayarlarından bu bilgisayara sabit IP verin (DHCP rezervasyonu). Ayrıntılar kurulum rehberinde.</p>
        </>
      )}
    </section>
  );
}

function Backups({ settings }: { settings: SettingsRow }) {
  const qc = useQueryClient();
  const backups = useQuery({ queryKey: ['admin', 'backups'], queryFn: adminApi.backups });
  const [error, setError] = useState<string | null>(null);
  const create = useMutation({
    mutationFn: adminApi.createBackup,
    onSuccess: () => {
      setError(null);
      qc.invalidateQueries({ queryKey: ['admin', 'backups'] });
      qc.invalidateQueries({ queryKey: ['admin', 'settings'] });
    },
    onError: (e) => setError(e.message),
  });

  return (
    <section>
      <div className="toolbar">
        <h2>Yedekler</h2>
        <button className="primary" disabled={create.isPending} onClick={() => create.mutate()}>
          {create.isPending ? 'Yedekleniyor…' : 'Şimdi yedek al'}
        </button>
      </div>
      <p className="hint">
        Her gün otomatik yedek alınır, 30 gün saklanır. Klasör: <code>{settings.effectiveBackupDir}</code>
      </p>
      {settings.lastBackupError && <p className="error">Son yedek başarısız: {settings.lastBackupError}</p>}
      {error && <p className="error" role="alert">{error}</p>}
      {backups.data && backups.data.length === 0 && <p className="muted">Henüz yedek yok.</p>}
      <ul className="admin-list">
        {backups.data?.slice(0, 10).map((b) => (
          <li key={b.fileName}>
            <a className="row-btn backup-link" href={`/api/admin/backups/${b.fileName}`} download>
              <span className="row-main">
                <span>{formatDateTime(b.createdAt)}</span>
                <small>{b.fileName} · {(b.sizeBytes / 1024).toFixed(0)} KB · indir</small>
              </span>
            </a>
          </li>
        ))}
      </ul>
    </section>
  );
}
```

- [ ] **Step 4: Sekmeyi ve giriş başlığını bağla**

`web/src/pages/admin/AdminPage.tsx` içinde:
- importlara `import { SettingsAdmin } from './SettingsAdmin';` ekle.
- `<NavLink to="kullanicilar">Kullanıcılar</NavLink>` satırının altına `<NavLink to="ayarlar">Ayarlar</NavLink>` ekle.
- `<Route path="kullanicilar" element={<UsersAdmin />} />` satırının altına `<Route path="ayarlar" element={<SettingsAdmin />} />` ekle.

`web/src/pages/LoginPage.tsx` içinde:
- `LoginPage` fonksiyonunun başına ekle:

```tsx
  const info = useQuery({ queryKey: ['info'], queryFn: () => api<{ businessName: string }>('/info') });
  const title = info.data?.businessName ?? 'Fırın Adisyon';
  useEffect(() => { document.title = title; }, [title]);
```

- `useEffect`'i react importuna ekle: `import { useEffect, useState, type FormEvent } from 'react';`.
- `return <UserLogin users={status.data.users} />;` satırını `return <UserLogin title={title} users={status.data.users} />;` yap. `UserLogin` imzasını `function UserLogin({ title, users }: { title: string; users: UserOption[] })` yap ve içindeki `<h1>Fırın Adisyon</h1>` satırını `<h1>{title}</h1>` yap.

- [ ] **Step 5: CSS ekle**

`web/src/styles.css` sonuna ekle:

```css
.settings section { margin-bottom: 32px; }
.settings h2 { font-size: 1.15rem; margin: 0 0 8px; }
.ok { color: var(--ok); }
.qr-list { display: flex; flex-wrap: wrap; gap: 16px; }
.qr { margin: 0; padding: 12px; background: #fff; border: 1px solid var(--border); border-radius: var(--radius); text-align: center; }
.qr figcaption { margin-top: 8px; font-family: ui-monospace, Consolas, monospace; font-size: 0.9rem; user-select: all; }
.backup-link { text-decoration: none; color: inherit; border: 1px solid var(--border); border-radius: var(--radius); background: var(--surface); }
code { overflow-wrap: anywhere; }
```

- [ ] **Step 6: Derle ve dene**

```bash
cd "C:/Users/Ali G/Desktop/firin-adisyon/web"
npm run build
cd ..
dotnet run --project server/Firin.Api
```

Sunucu arka planda çalışırken sahip olarak gir → Yönetim → Ayarlar:
1. Kasa PC'nin yerel ağ adresi (örn. `http://192.168.1.20:5080`) QR koduyla görünür.
2. İşletme adı değiştirilip kaydedilir. "Kullanıcı değiştir" yapılınca giriş ekranı başlığında yeni ad görünür.
3. "Şimdi yedek al"a basılır. Listeye satır eklenir. Satıra tıklanınca `.db` dosyası iner.
4. Yedek klasörüne `yedekler` yazılıp kaydedilince "Yedek klasörü tam yol olmalı…" uyarısı çıkar.
5. Sunucu ilk açıldığında `server/Firin.Api/data/backups/` içinde otomatik bir yedek oluşmuş olur (çünkü hiç yedek yoktu).

Sunucuyu durdur.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: ayarlar ekranı, tablet bağlantı QR'ı ve yedek listesi"
```

### Task 3: Telefon ve tablette uygulama hissi

**Files:**
- Create: `web/public/manifest.webmanifest`, `web/public/icon.svg`, `scripts/make-icons.ps1`, `web/public/icon-192.png`, `web/public/icon-512.png`, `web/public/apple-touch-icon.png` (son üçü betik üretir)
- Modify: `web/index.html`, `web/src/styles.css`

**Interfaces:**
- Produces: "Ana ekrana ekle" ile açılınca adres çubuğu olmayan tam ekran uygulama (`display: standalone`), simge ve başlık "Adisyon"

Bu task'taki her değişikliğin bir sebebi var; aşağıdaki tablo hangi belirtiyi giderdiğini söyler. Masaüstü tarayıcının cihaz öykünmesi bu sorunların çoğunu göstermez. Son doğrulama Task 4'te gerçek tablet ve telefonda yapılır.

| Belirti | Çözüm |
|---|---|
| Dokununca gri/mavi parlama | `html { -webkit-tap-highlight-color: transparent }` (body'den html'e taşınır) |
| Dokunduktan sonra hover rengi takılı kalıyor (rapor sütunları) | `:hover` yalnızca `@media (hover: hover) and (pointer: fine)` içinde |
| Dokunuş gecikmeli hissediliyor | `touch-action: manipulation` + `:active` ile basınca küçülme |
| Aşağı çekince sayfa yenileniyor (tezgâhta yanlışlıkla) | `html, body { overscroll-behavior: none }`; iç kaydırmalılarda `contain` |
| Uzun basınca düğme yazısı seçiliyor | Kontrollerde `user-select: none` + `-webkit-touch-callout: none` |
| Android'de klavye açılınca diyalog altta kalıyor | viewport'a `interactive-widget=resizes-content` |
| Yatay tutunca çentik içeriği kesiyor | Başlık ve diyaloglarda `env(safe-area-inset-*)` |
| Durum çubuğu rengi başlıkla uyumsuz | `theme-color` = başlık arka planı `#ffffff` |

- [ ] **Step 1: index.html'i güncelle**

`web/index.html` içindeki `<head>` bloğunu şununla değiştir:

```html
  <head>
    <meta charset="UTF-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover, interactive-widget=resizes-content" />
    <meta name="theme-color" content="#ffffff" />
    <meta name="color-scheme" content="light" />
    <meta name="mobile-web-app-capable" content="yes" />
    <meta name="apple-mobile-web-app-capable" content="yes" />
    <meta name="apple-mobile-web-app-status-bar-style" content="default" />
    <meta name="apple-mobile-web-app-title" content="Adisyon" />
    <link rel="manifest" href="/manifest.webmanifest" />
    <link rel="icon" href="/icon.svg" type="image/svg+xml" />
    <link rel="apple-touch-icon" href="/apple-touch-icon.png" />
    <title>Fırın Adisyon</title>
  </head>
```

Uygulamada yalnızca açık tema olduğu için tek `theme-color` yeterli.

- [ ] **Step 2: Manifest ve simgeyi yaz**

`web/public/manifest.webmanifest`:

```json
{
  "name": "Fırın Adisyon",
  "short_name": "Adisyon",
  "lang": "tr",
  "start_url": "/",
  "scope": "/",
  "display": "standalone",
  "orientation": "any",
  "background_color": "#faf6f0",
  "theme_color": "#ffffff",
  "icons": [
    { "src": "/icon-192.png", "sizes": "192x192", "type": "image/png" },
    { "src": "/icon-512.png", "sizes": "512x512", "type": "image/png" },
    { "src": "/icon-512.png", "sizes": "512x512", "type": "image/png", "purpose": "maskable" },
    { "src": "/icon.svg", "sizes": "any", "type": "image/svg+xml" }
  ]
}
```

`web/public/icon.svg` (kahverengi zemin üzerinde çizikli somun ekmek):

```svg
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512">
  <rect width="512" height="512" rx="96" fill="#b5651d"/>
  <ellipse cx="256" cy="276" rx="170" ry="108" fill="#fff5e6"/>
  <g stroke="#b5651d" stroke-width="22" stroke-linecap="round">
    <line x1="176" y1="236" x2="212" y2="316"/>
    <line x1="238" y1="226" x2="274" y2="326"/>
    <line x1="300" y1="236" x2="336" y2="316"/>
  </g>
</svg>
```

- [ ] **Step 3: PNG simgeleri üret**

Android kurulumu ve iOS ana ekranı PNG ister. Windows'taki System.Drawing ile aynı çizimi PNG'ye döken betik:

`scripts/make-icons.ps1`:

```powershell
# web/public altına icon-192.png, icon-512.png ve apple-touch-icon.png (180) üretir.
# icon.svg ile aynı çizim: kahverengi yuvarlak kare + krem somun + üç çizik.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$out = Join-Path (Split-Path $PSScriptRoot -Parent) 'web\public'

function New-Icon([int]$size, [string]$name) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $s = $size / 512.0
    $brown = [System.Drawing.ColorTranslator]::FromHtml('#b5651d')
    $cream = [System.Drawing.ColorTranslator]::FromHtml('#fff5e6')

    $r = 96 * $s; $d = 2 * $r
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddArc(0, 0, $d, $d, 180, 90)
    $path.AddArc($size - $d, 0, $d, $d, 270, 90)
    $path.AddArc($size - $d, $size - $d, $d, $d, 0, 90)
    $path.AddArc(0, $size - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $g.FillPath((New-Object System.Drawing.SolidBrush $brown), $path)

    $g.FillEllipse((New-Object System.Drawing.SolidBrush $cream), (86 * $s), (168 * $s), (340 * $s), (216 * $s))

    $pen = New-Object System.Drawing.Pen $brown, (22 * $s)
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    foreach ($l in @(@(176, 236, 212, 316), @(238, 226, 274, 326), @(300, 236, 336, 316))) {
        $g.DrawLine($pen, ($l[0] * $s), ($l[1] * $s), ($l[2] * $s), ($l[3] * $s))
    }

    $bmp.Save((Join-Path $out $name), [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Host "Yazıldı: $name"
}

New-Icon 192 'icon-192.png'
New-Icon 512 'icon-512.png'
New-Icon 180 'apple-touch-icon.png'
```

Run (PowerShell): `powershell -ExecutionPolicy Bypass -File scripts/make-icons.ps1`
Expected: üç "Yazıldı:" satırı. Ardından `web/public/icon-512.png` dosyasını Read aracıyla açıp simgenin SVG ile aynı göründüğünü kontrol et.

- [ ] **Step 4: Mobil CSS düzeltmelerini uygula**

`web/src/styles.css` içinde:

1. `body { margin: 0; background: var(--bg); -webkit-tap-highlight-color: transparent; }` satırını şununla değiştir:

```css
html {
  -webkit-tap-highlight-color: transparent;
  -webkit-text-size-adjust: 100%;
  overscroll-behavior: none;
}
body { margin: 0; background: var(--bg); overscroll-behavior: none; }
```

2. `button { ... }` kuralındaki `user-select: none;` satırının altına ekle:

```css
  -webkit-user-select: none;
  -webkit-touch-callout: none;
  transition: transform 100ms ease-out, background-color 100ms ease-out;
```

3. `.col-slot.active .col-bar, .col-slot:hover .col-bar { filter: brightness(0.85); }` satırını şununla değiştir:

```css
.col-slot.active .col-bar { filter: brightness(0.85); }
```

4. Dosyanın sonuna ekle:

```css
/* Kontroller: anında dokunma, uzun basınca seçim/menü yok. İçerik metni seçilebilir kalır. */
a, [role='button'] { touch-action: manipulation; }
.shell-header nav a, .tabs a, .chip, .row-btn, .table-card, .product-card {
  user-select: none;
  -webkit-user-select: none;
  -webkit-touch-callout: none;
}

/* Basınca geri bildirim: her giriş türünde çalışır. */
button:active:not(:disabled) { transform: scale(0.97); }
.table-card:active, .product-card:active { background: var(--busy); }

/* Hover yalnızca fare gibi hassas ve hover yapabilen işaretçilerde; dokunmatikte takılı kalmaz. */
@media (hover: hover) and (pointer: fine) {
  button:hover:not(:disabled) { border-color: var(--accent); }
  .col-slot:hover .col-bar { filter: brightness(0.85); }
  .shell-header nav a:hover, .tabs a:hover { color: var(--accent); }
}

/* İç kaydırma alanları sona gelince sayfayı sürüklemesin. */
.modal, .pane-order, .pick-items, .chips, .tabs, .shell-header nav { overscroll-behavior: contain; }

/* Çentik ve ev çubuğu boşlukları (viewport-fit=cover ile birlikte çalışır). */
.shell-header {
  padding-left: max(16px, env(safe-area-inset-left, 0px));
  padding-right: max(16px, env(safe-area-inset-right, 0px));
}
main {
  padding-left: max(16px, env(safe-area-inset-left, 0px));
  padding-right: max(16px, env(safe-area-inset-right, 0px));
  padding-bottom: calc(16px + env(safe-area-inset-bottom, 0px));
}
.modal { padding-bottom: calc(16px + env(safe-area-inset-bottom, 0px)); }
```

`.row-btn` kuralı Task 2'deki `<a className="row-btn backup-link">` için de geçerlidir. Yedek bağlantısında uzun basınca "bağlantıyı kopyala" menüsü çıkmaz.

- [ ] **Step 5: Ekran genişliklerinde kontrol et**

```bash
cd "C:/Users/Ali G/Desktop/firin-adisyon/web"
npm test
npm run build
cd ..
dotnet run --project server/Firin.Api
```

Sunucu arka planda çalışırken Chrome eklentisiyle (`mcp__claude-in-chrome__*`) `http://localhost:5080` sayfasını 394, 800 ve 1280 px genişliklerde aç. Şu sayfaların her birinde ekran görüntüsü al: giriş, masalar, masa detayı (ürünler ve hesap sekmesi), ödeme diyaloğu, gram tuş takımı, Yönetim → Ürünler, ürün formu, Raporlar (Bu hafta), Ayarlar. Her sayfada şu JavaScript ile yatay taşmayı ölç:

```js
document.documentElement.scrollWidth <= window.innerWidth
```

Expected: her sayfada `true`. Görüntülerde yazı çakışması, kesilen tutar ya da ekrandan taşan düğme yok. Sorun çıkan sayfayı düzelt, yeniden ölç.

Sunucuyu durdur.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: PWA manifest, simgeler ve mobil dokunma düzeltmeleri"
```

### Task 4: Yayın, Windows servisi ve kurulum rehberi

**Files:**
- Create: `scripts/publish.ps1`, `scripts/install-service.ps1`, `scripts/uninstall-service.ps1`, `docs/KURULUM.md`, `README.md`
- Modify: `.gitignore`

**Interfaces:**
- Produces: `publish\FirinAdisyon\Firin.Api.exe` (self-contained, .NET kurulumu gerekmez); `FirinAdisyon` Windows servisi (otomatik başlar, çökerse yeniden başlar); `Firin Adisyon 5080` güvenlik duvarı kuralı (Özel ve Etki alanı ağları)

- [ ] **Step 1: Yayın betiğini yaz**

`.gitignore` sonuna `publish/` satırının zaten olduğunu kontrol et (Parça 1'de eklendi).

`scripts/publish.ps1`:

```powershell
# Web'i derler, testleri çalıştırır, kasa PC için self-contained yayın üretir.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent

Push-Location (Join-Path $root 'web')
try {
    npm ci
    if ($LASTEXITCODE) { throw 'npm ci başarısız' }
    npm test
    if ($LASTEXITCODE) { throw 'web testleri başarısız' }
    npm run build
    if ($LASTEXITCODE) { throw 'web derlemesi başarısız' }
} finally { Pop-Location }

dotnet test (Join-Path $root 'server\Firin.Tests')
if ($LASTEXITCODE) { throw 'sunucu testleri başarısız' }

$out = Join-Path $root 'publish\FirinAdisyon'
dotnet publish (Join-Path $root 'server\Firin.Api') -c Release -r win-x64 --self-contained true -o $out
if ($LASTEXITCODE) { throw 'yayın başarısız' }

if (-not (Test-Path (Join-Path $out 'wwwroot\index.html'))) { throw 'wwwroot yayına girmedi' }
Write-Host "Yayın hazır: $out"
```

Run: `powershell -ExecutionPolicy Bypass -File scripts/publish.ps1`
Expected: son satır `Yayın hazır: ...\publish\FirinAdisyon`. Klasörde `Firin.Api.exe` ve `wwwroot\index.html` bulunur.

- [ ] **Step 2: Servis kurulum betiklerini yaz**

`scripts/install-service.ps1`:

```powershell
#Requires -RunAsAdministrator
# Yayını C:\FirinAdisyon'a kopyalar, servisi kurar ya da günceller. data klasörüne dokunmaz.
param([string]$Target = 'C:\FirinAdisyon')
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$source = Join-Path $root 'publish\FirinAdisyon'
if (-not (Test-Path (Join-Path $source 'Firin.Api.exe'))) { throw 'Önce scripts\publish.ps1 çalıştırın.' }

$svc = Get-Service FirinAdisyon -ErrorAction SilentlyContinue
if ($svc -and $svc.Status -ne 'Stopped') {
    Stop-Service FirinAdisyon
    $svc.WaitForStatus('Stopped', '00:00:30')
}

New-Item -ItemType Directory -Force $Target | Out-Null
# /MIR eski dosyaları temizler; /XD ile veriler (veritabanı, fotoğraflar, yedekler) korunur.
robocopy $source $Target /MIR /XD (Join-Path $Target 'data') /NFL /NDL /NJH /NJS | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Dosyalar kopyalanamadı (robocopy kodu $LASTEXITCODE)" }

if (-not $svc) {
    New-Service -Name FirinAdisyon -BinaryPathName "`"$Target\Firin.Api.exe`"" `
        -DisplayName 'Fırın Adisyon' -StartupType Automatic `
        -Description 'Fırın masa adisyon sunucusu (port 5080)' | Out-Null
    # Çökerse 5 sn sonra yeniden başlat.
    sc.exe failure FirinAdisyon reset= 86400 actions= restart/5000/restart/5000/restart/60000 | Out-Null
}

if (-not (Get-NetFirewallRule -DisplayName 'Firin Adisyon 5080' -ErrorAction SilentlyContinue)) {
    New-NetFirewallRule -DisplayName 'Firin Adisyon 5080' -Direction Inbound -Protocol TCP -LocalPort 5080 `
        -Action Allow -Profile Private, Domain | Out-Null
}

$public = Get-NetConnectionProfile | Where-Object NetworkCategory -eq 'Public'
if ($public) {
    Write-Warning ("'{0}' ağı 'Ortak' olarak ayarlı; tabletler bağlanamaz. Ayarlar > Ağ ve İnternet > Wi-Fi/Ethernet > Ağ profili: Özel yapın." -f ($public.Name -join ', '))
}

# Kasa bilgisayarı fişe takılıyken uykuya geçmesin; uyursa tabletler bağlanamaz.
powercfg /change standby-timeout-ac 0

Start-Service FirinAdisyon
Write-Host "Kuruldu ve başlatıldı. Bu bilgisayarda: http://localhost:5080"
```

`scripts/uninstall-service.ps1`:

```powershell
#Requires -RunAsAdministrator
# Servisi ve güvenlik duvarı kuralını kaldırır. C:\FirinAdisyon\data (veriler) silinmez.
$ErrorActionPreference = 'Stop'
if (Get-Service FirinAdisyon -ErrorAction SilentlyContinue) {
    Stop-Service FirinAdisyon -ErrorAction SilentlyContinue
    sc.exe delete FirinAdisyon | Out-Null
}
Get-NetFirewallRule -DisplayName 'Firin Adisyon 5080' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
Write-Host 'Servis kaldırıldı. Veriler C:\FirinAdisyon\data altında duruyor.'
```

- [ ] **Step 3: Kurulum rehberini ve README'yi yaz**

`docs/KURULUM.md`:

````markdown
# Fırın Adisyon — Kurulum ve Kullanım Rehberi

## 1. Kasa bilgisayarına kurulum

1. Proje klasöründe PowerShell açıp yayını üretin:
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\publish.ps1
   ```
2. PowerShell'i **Yönetici olarak** açıp servisi kurun:
   ```powershell
   powershell -ExecutionPolicy Bypass -File scripts\install-service.ps1
   ```
   Betik şunları yapar:
   - Programı `C:\FirinAdisyon` klasörüne kopyalar.
   - "Fırın Adisyon" servisini kurar. Servis bilgisayar açılınca kendiliğinden başlar.
   - 5080 portu için güvenlik duvarı izni ekler.
   - Fişe takılıyken bilgisayarın uykuya geçmesini kapatır.
3. Tarayıcıda `http://localhost:5080` adresini açın. İlk açılışta sahip hesabı (ad ve 4 haneli PIN) oluşturulur.

Güncelleme için aynı iki komutu yeniden çalıştırın. Veriler (`C:\FirinAdisyon\data`) korunur.

## 2. Sabit IP (tavsiye edilir)

Tabletler kasa bilgisayarına IP adresiyle bağlanır. Adres değişirse tabletler bağlanamaz.
- Modemin yönetim sayfasına girin (genellikle `192.168.1.1`).
- "DHCP" ya da "LAN" ayarlarında "Adres rezervasyonu / Sabit DHCP" bölümünü bulun.
- Kasa bilgisayarını listeden seçip şu anki adresini sabitleyin.

Windows'ta ağ profili **Özel** olmalıdır: Ayarlar → Ağ ve İnternet → Wi-Fi (ya da Ethernet) → ağ adı → Ağ profili türü: Özel.

## 3. Tablet ve telefonu bağlama

1. Cihazı fırının Wi-Fi ağına bağlayın.
2. Kasada Yönetim → Ayarlar sayfasını açın ve QR kodu cihazın kamerasıyla okutun.
3. Uygulamayı ana ekrana ekleyin:
   - **Android (Chrome):** sağ üst menü (⋮) → "Ana ekrana ekle" ya da "Uygulamayı yükle".
   - **iPhone / iPad (Safari):** Paylaş düğmesi → "Ana Ekrana Ekle".
4. Ana ekrandaki "Adisyon" simgesiyle açın. Her garson kendi adını seçip PIN'iyle girer.

## 4. Günlük kullanım

- **Masalar** ekranında dolu masalar renkli görünür. Kartta kalan tutar ve masanın açık kaldığı süre yazar.
- Masaya dokunup ürün ekleyin. Gramla satılan ürünlerde tartıdaki gramı tuş takımına yazın.
- **Ödeme al** ile ödemeyi tamamını, bir tutarı, kişi başı ya da ürün seçerek alın. Kalan sıfırlanınca masa boşalır.
- Kırmızı "bağlantı yok" bandı çıkarsa:
  - Kasa bilgisayarının açık olduğunu kontrol edin.
  - Cihazın fırının Wi-Fi ağında olduğunu kontrol edin.
  - Bağlantı gelince band kendiliğinden kaybolur.

## 5. Yedekler

- Program her gün otomatik yedek alır ve 30 gün saklar. Yönetim → Ayarlar'dan elle de yedek alabilir, indirebilirsiniz.
- Yedek klasörü olarak bir **USB bellek ya da harici disk** seçmeniz tavsiye edilir (örn. `E:\FirinYedek`). Bilgisayar bozulsa bile veriler kurtulur.

### Yedekten geri dönme

1. PowerShell'i Yönetici olarak açın: `Stop-Service FirinAdisyon`
2. `C:\FirinAdisyon\data\firin.db` dosyasının yanındaki varsa `firin.db-wal` ve `firin.db-shm` dosyalarını silin.
3. Geri dönmek istediğiniz yedeği (örn. `firin-20261021-060000.db`) `C:\FirinAdisyon\data\firin.db` adıyla kopyalayın. Mevcut dosyanın üzerine yazın.
4. `Start-Service FirinAdisyon`

Yedek dosya adındaki saat UTC'dir (Türkiye saatinden 3 saat geri). Ayarlar sayfası yedekleri Türkiye saatiyle listeler.

## 6. Kaldırma

`scripts\uninstall-service.ps1` betiğini Yönetici olarak çalıştırın. Veriler silinmez.
````

`README.md`:

```markdown
# Fırın Adisyon

Fırın ve kafe için masa adisyon uygulaması. Kasa bilgisayarında (Windows) çalışan bir sunucudan oluşur; tablet ve telefonlar fırının Wi-Fi ağından tarayıcıyla bağlanır.

- Kurulum ve kullanım: [docs/KURULUM.md](docs/KURULUM.md)
- Tasarım: [docs/superpowers/specs/2026-10-21-firin-adisyon-design.md](docs/superpowers/specs/2026-10-21-firin-adisyon-design.md)

## Geliştirme

    dotnet run --project server/Firin.Api      # http://localhost:5080
    cd web && npm run dev                      # http://localhost:5173 (API'ye proxy)
    dotnet test server/Firin.Tests
    cd web && npm test

Teknoloji: ASP.NET Core 10 Minimal API, EF Core + SQLite, SignalR; React + TypeScript + Vite, TanStack Query.
```

- [ ] **Step 4: Kasa PC'de kur ve gerçek cihazlarda dene**

Servis kurulumu yönetici izni ister. Kullanıcıdan Yönetici PowerShell'de şu komutu çalıştırmasını iste:

```powershell
powershell -ExecutionPolicy Bypass -File "C:\Users\Ali G\Desktop\firin-adisyon\scripts\install-service.ps1"
```

Ardından kontrol et:
1. `Get-Service FirinAdisyon` → `Running`.
2. `http://localhost:5080` açılır. Geliştirme sırasındaki veriler gelmez, çünkü veri klasörü artık `C:\FirinAdisyon\data`. İlk kurulum ekranı görünür.
3. Bilgisayar yeniden başlatılınca servis kendiliğinden ayağa kalkar.

Gerçek tablet ve telefonda (kullanıcıyla birlikte; masaüstü öykünmesi bu maddeleri göstermez):
1. Ayarlar'daki QR okutulur, sayfa açılır, ana ekrana eklenir. Simgeyle açılınca adres çubuğu görünmez.
2. Düğmelere dokununca gri parlama olmaz. Basınca düğme hafifçe küçülür, bırakınca geri gelir. Rapor sütununa dokununca renk takılı kalmaz.
3. PIN, fiyat ve gram alanlarına dokununca sayfa yakınlaşmaz. Sayısal klavye açılır.
4. Masalar ekranında aşağı çekince sayfa yenilenmez.
5. Uzun basınca düğme yazısı seçilmez.
6. Ödeme diyaloğunda "Tutar gir" alanına dokununca klavye açılır ve Nakit/Kart düğmeleri klavyenin üstünde görünür kalır (Android).
7. Tablet yatay ve dikey çevrilir. Yatayda hesap ve ürünler yan yana durur. Çentikli telefonda başlık çentiğin altında kalmaz.
8. Tablet 5 dakika uyku moduna bırakılıp açılır. Birkaç saniye içinde bağlantı bandı kaybolur. Bu arada kasada eklenen kalemler görünür.
9. Kasa PC'nin Wi-Fi'ı kapatılınca tablette kırmızı band çıkar ve düğmeler kilitlenir. Açılınca düzelir.

Sorun çıkan maddeyi not edip ilgili CSS/kodu düzelt ve yeniden dene.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat: yayın ve Windows servis betikleri, kurulum rehberi"
```
