using System.Net;
using System.Net.Http.Json;
using System.Text;
using Firin.Api.Backups;
using Firin.Api.Domain;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
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
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/backups")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await staff.GetAsync("/api/admin/backups/firin-20261021-060000.db")).StatusCode);
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
        Assert.Empty(Directory.GetFiles(DefaultBackupDir, "*.tmp"));
    }

    [Fact]
    public async Task Failed_backup_leaves_no_file_and_reports_coded_error()
    {
        var owner = await _app.OwnerAsync();
        // Klasör yolunun üstünde bir dosya var: CreateDirectory başarısız olur.
        var blocker = Path.Combine(_app.DataDir, "engel");
        await File.WriteAllTextAsync(blocker, "x");
        _app.Db(db => { db.Settings.Add(new Setting { Key = "BackupDir", Value = Path.Combine(blocker, "alt") }); db.SaveChanges(); return 0; });

        var res = await owner.PostAsync("/api/admin/backups", null);
        Assert.Equal(HttpStatusCode.InternalServerError, res.StatusCode);
        Assert.Equal("yedek_hatasi", await res.ProblemCode());

        var s = await (await owner.GetAsync("/api/admin/settings")).ReadAs<SettingsDto>();
        Assert.NotNull(s.LastBackupError);
    }

    [Fact]
    public async Task Partial_tmp_file_is_never_listed_or_downloadable()
    {
        var owner = await _app.OwnerAsync();
        Directory.CreateDirectory(DefaultBackupDir);
        await File.WriteAllTextAsync(Path.Combine(DefaultBackupDir, "firin-20261021-050000.db.tmp"), "yarim");

        Assert.Empty(await (await owner.GetAsync("/api/admin/backups")).ReadAs<List<BackupDto>>());
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/api/admin/backups/firin-20261021-050000.db.tmp")).StatusCode);
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
    public async Task Unwritable_backup_dir_is_rejected()
    {
        var owner = await _app.OwnerAsync();
        var blocker = Path.Combine(_app.DataDir, "dosya");
        await File.WriteAllTextAsync(blocker, "x");
        var res = await owner.PutAsJsonAsync("/api/admin/settings", new { businessName = (string?)null, backupDir = Path.Combine(blocker, "alt") });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("klasor_yazilamiyor", await res.ProblemCode());
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
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/api/admin/backups/..%2Ffirin.db")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/api/admin/backups/..%5Cfirin-20261021-060000.db")).StatusCode);
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

    [Fact]
    public async Task Last_backup_is_read_from_disk_when_none_taken_this_run()
    {
        var owner = await _app.OwnerAsync();
        Directory.CreateDirectory(DefaultBackupDir);
        await File.WriteAllTextAsync(Path.Combine(DefaultBackupDir, "firin-20261020-010203.db"), "x");
        var s = await (await owner.GetAsync("/api/admin/settings")).ReadAs<SettingsDto>();
        Assert.Equal(new DateTime(2026, 10, 20, 1, 2, 3, DateTimeKind.Utc), s.LastBackupAt);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"businessName\":5}")]
    public async Task Malformed_settings_body_gives_coded_400(string body)
    {
        var owner = await _app.OwnerAsync();
        var res = await owner.PutAsync("/api/admin/settings", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("istek_gecersiz", await res.ProblemCode());
    }

    [Fact]
    public async Task Auto_backup_runs_on_start_and_again_after_a_day()
    {
        using var app = new TestApp(configure: null, autoBackup: true);
        var dir = Path.Combine(app.DataDir, "backups");
        await WaitFor(() => Directory.Exists(dir) && Directory.GetFiles(dir, "firin-*.db").Length == 1);

        app.Clock.Advance(TimeSpan.FromHours(23));
        await Task.Delay(300);
        Assert.Single(Directory.GetFiles(dir, "firin-*.db"));

        for (var i = 0; i < 2; i++) { app.Clock.Advance(TimeSpan.FromHours(1)); await Task.Delay(150); }
        await WaitFor(() => Directory.GetFiles(dir, "firin-*.db").Length == 2);
    }

    private static async Task WaitFor(Func<bool> cond)
    {
        for (var i = 0; i < 100 && !cond(); i++) await Task.Delay(50);
        Assert.True(cond(), "Beklenen durum oluşmadı");
    }

    [Fact]
    public async Task Orphan_photos_are_swept_but_referenced_and_fresh_ones_kept()
    {
        var owner = await _app.OwnerAsync();
        var cat = await Seed.CategoryAsync(owner, "Pasta");
        var p = await Seed.ProductAsync(owner, cat.Id, "Yaş pasta", UnitType.Piece, 9000);
        var photos = Path.Combine(_app.DataDir, "photos");
        var old = _app.Clock.GetUtcNow().UtcDateTime.AddHours(-2);

        string Plant(string name, DateTime mtime)
        {
            var f = Path.Combine(photos, name);
            File.WriteAllText(f, "x");
            File.SetLastWriteTimeUtc(f, mtime);
            return f;
        }
        var referenced = Plant("1-ref.jpg", old);
        _app.Db(db => { db.Products.Find(p.Id)!.PhotoFile = "1-ref.jpg"; db.SaveChanges(); return 0; });
        var orphan = Plant("2-yetim.jpg", old);
        var fresh = Plant("3-yeni.jpg", _app.Clock.GetUtcNow().UtcDateTime.AddMinutes(-5));

        var removed = await _app.Factory.Services.GetRequiredService<PhotoCleanup>().SweepAsync();

        Assert.Equal(1, removed);
        Assert.False(File.Exists(orphan));
        Assert.True(File.Exists(referenced));
        Assert.True(File.Exists(fresh));
    }
}
