using System.Globalization;
using System.Text.RegularExpressions;
using Firin.Api.Settings;
using Microsoft.Data.Sqlite;

namespace Firin.Api.Backups;

public record BackupDto(string FileName, long SizeBytes, DateTime CreatedAt);

/// <summary>
/// Çevrimiçi SQLite yedeği. BackupDatabase tek okuma işleminde tutarlı bir kopya alır, bu yüzden OrderLock gerekmez;
/// WAL açıkken yazanları da bloklamaz.
/// </summary>
public sealed partial class BackupService(FirinPaths paths, IServiceScopeFactory scopes, TimeProvider clock, ILogger<BackupService> log)
{
    public const int KeepDays = 30;
    private const string StampFormat = "yyyyMMdd-HHmmss";
    private readonly SemaphoreSlim _lock = new(1, 1);
    private DateTime? _lastBackupAt;

    [GeneratedRegex(@"^firin-(\d{8}-\d{6})\.db$")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^firin-\d{8}-\d{6}\.db\.tmp$")]
    private static partial Regex TmpPattern();

    public string? LastError { get; private set; }

    /// <summary>Bu oturumda alınan son yedek; yeniden başlatma sonrası klasördeki en yeni yedeğe bakılır.</summary>
    public async Task<DateTime?> GetLastBackupAtAsync() =>
        _lastBackupAt ?? List(await ResolveDirAsync()).FirstOrDefault()?.CreatedAt;

    public async Task<string> ResolveDirAsync()
    {
        using var scope = scopes.CreateScope();
        var custom = await scope.ServiceProvider.GetRequiredService<SettingsService>().GetAsync(SettingsService.BackupDirKey);
        return string.IsNullOrWhiteSpace(custom) ? paths.BackupsDir : custom;
    }

    public async Task<BackupDto> CreateAsync()
    {
        await _lock.WaitAsync();
        string? tmp = null;
        try
        {
            var dir = await ResolveDirAsync();
            Directory.CreateDirectory(dir);
            var now = clock.GetUtcNow().UtcDateTime;
            var name = $"firin-{now.ToString(StampFormat, CultureInfo.InvariantCulture)}.db";
            var path = Path.Combine(dir, name);
            tmp = path + ".tmp";

            // Önce .tmp'ye yazılır; yalnızca başarıda asıl ada taşınır. Yarım kalan dosya yedek gibi görünmez.
            var target = new SqliteConnectionStringBuilder { DataSource = tmp, Pooling = false }.ToString();
            await using (var src = new SqliteConnection(paths.ConnectionString))
            await using (var dst = new SqliteConnection(target))
            {
                await src.OpenAsync();
                await dst.OpenAsync();
                src.BackupDatabase(dst);
            }
            File.Move(tmp, path, overwrite: true);
            tmp = null;

            Prune(dir, now);
            _lastBackupAt = now;
            LastError = null;
            return new BackupDto(name, new FileInfo(path).Length, now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or ArgumentException or NotSupportedException)
        {
            LastError = ex.Message;
            log.LogError(ex, "Yedek alınamadı");
            throw new DomainException("yedek_hatasi", $"Yedek alınamadı: {ex.Message}", StatusCodes.Status500InternalServerError);
        }
        finally
        {
            if (tmp is not null)
            {
                try { File.Delete(tmp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
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
        // Çökme ya da elektrik kesintisiyle kalmış yarım yedekler.
        foreach (var t in Directory.EnumerateFiles(dir, "firin-*.tmp").Where(t => TmpPattern().IsMatch(Path.GetFileName(t))))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(t) < nowUtc.AddHours(-1)) File.Delete(t);
            }
            catch (IOException ex) { log.LogWarning(ex, "Yarım yedek silinemedi: {File}", t); }
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
