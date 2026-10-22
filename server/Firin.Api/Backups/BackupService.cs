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

    [GeneratedRegex(@"^firin-([0-9]{8}-[0-9]{6})\.db\z")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^firin-[0-9]{8}-[0-9]{6}\.db\.tmp(-journal|-wal|-shm)?\z")]
    private static partial Regex TmpPattern();

    public string? LastError { get; private set; }

    public void ClearError() => LastError = null;

    public const string FallbackMessage = "Yedek klasörüne ulaşılamadı; yedek varsayılan klasöre alındı.";

    /// <summary>Bu oturumda alınan son yedek; yeniden başlatma sonrası klasörlerdeki en yeni yedeğe bakılır.</summary>
    public async Task<DateTime?> GetLastBackupAtAsync() =>
        _lastBackupAt ?? (await ListAsync()).FirstOrDefault()?.CreatedAt;

    /// <summary>Ayarlanan yedek klasörü (boşsa varsayılan). Yedek alınamazsa varsayılana düşülür, bkz. CreateAsync.</summary>
    public async Task<string> ResolveDirAsync()
    {
        using var scope = scopes.CreateScope();
        var custom = await scope.ServiceProvider.GetRequiredService<SettingsService>().GetAsync(SettingsService.BackupDirKey);
        return string.IsNullOrWhiteSpace(custom) ? paths.BackupsDir : custom;
    }

    /// <summary>Ayarlı klasör ile varsayılan klasördeki yedekler birlikte, yeniden eskiye. Yedek geri düştüyse de görünür kalır.</summary>
    public async Task<List<BackupDto>> ListAsync()
    {
        var dir = await ResolveDirAsync();
        var all = List(dir);
        if (!SamePath(dir, paths.BackupsDir)) all.AddRange(List(paths.BackupsDir));
        return all.DistinctBy(b => b.FileName).OrderByDescending(b => b.CreatedAt).ToList();
    }

    /// <summary>
    /// Önce ayarlı klasöre yazar. Ulaşılamıyorsa (USB çıkarıldı, sürücü harfi değişti) aynı yedeği varsayılan klasöre alır,
    /// LastError'u Türkçe açıklamayla bırakır ve yine başarılı döner; hiç yedeksiz kalınmaz. İkisi de olmazsa yedek_hatasi.
    /// </summary>
    public async Task<BackupDto> CreateAsync()
    {
        await _lock.WaitAsync();
        try
        {
            var dir = await ResolveDirAsync();
            var now = clock.GetUtcNow().UtcDateTime;
            try
            {
                var dto = await WriteAsync(dir, now);
                LastError = null;
                return dto;
            }
            catch (Exception ex) when (IsFileError(ex) && !SamePath(dir, paths.BackupsDir))
            {
                log.LogWarning(ex, "Yedek klasörüne yazılamadı ({Dir}); varsayılan klasöre alınıyor", dir);
                var dto = await WriteAsync(paths.BackupsDir, now);
                LastError = FallbackMessage;
                return dto;
            }
        }
        catch (Exception ex) when (IsFileError(ex))
        {
            LastError = $"Yedek alınamadı: {Explain(ex)}";
            log.LogError(ex, "Yedek alınamadı");
            throw new DomainException("yedek_hatasi", LastError, StatusCodes.Status500InternalServerError);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<BackupDto> WriteAsync(string dir, DateTime now)
    {
        string? tmp = null;
        try
        {
            Directory.CreateDirectory(dir);
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
                // Kaynak WAL'dedir; yedek tek başına, yanında -wal/-shm istemeyen bir dosya olsun (salt okunur ortamda da açılır).
                await using var pragma = dst.CreateCommand();
                pragma.CommandText = "PRAGMA journal_mode=DELETE;";
                await pragma.ExecuteNonQueryAsync();
            }
            File.Move(tmp, path, overwrite: true);
            tmp = null;

            Prune(dir, now);
            _lastBackupAt = now;
            return new BackupDto(name, new FileInfo(path).Length, now);
        }
        finally
        {
            if (tmp is not null)
            {
                try { File.Delete(tmp); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private static bool IsFileError(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or SqliteException or ArgumentException or NotSupportedException;

    private static bool SamePath(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>.NET istisna metinleri İngilizcedir; kullanıcıya kısa Türkçe karşılığı gösterilir (özgün hata günlüğe yazılır).</summary>
    public static string Explain(Exception ex) => ex switch
    {
        DirectoryNotFoundException => "klasör ya da sürücü bulunamadı.",
        UnauthorizedAccessException => "klasöre yazma izni yok.",
        SqliteException => "veritabanı okunamadı.",
        ArgumentException or NotSupportedException => "klasör yolu geçersiz.",
        IOException => "klasöre yazılamadı (disk dolu, çıkarılmış ya da kullanımda olabilir).",
        _ => "beklenmeyen bir hata oluştu.",
    };

    public List<BackupDto> List(string dir)
    {
        if (!Directory.Exists(dir)) return [];
        var list = new List<BackupDto>();
        foreach (var p in Directory.EnumerateFiles(dir, "firin-*.db"))
        {
            if (StampOf(Path.GetFileName(p)) is not { } stamp) continue;
            try { list.Add(new BackupDto(Path.GetFileName(p), new FileInfo(p).Length, stamp)); }
            catch (FileNotFoundException) { } // listelerken silindi
        }
        return list.OrderByDescending(b => b.CreatedAt).ToList();
    }

    public async Task<string?> PathOfAsync(string name)
    {
        if (StampOf(name) is null) return null; // yalnızca bizim adlandırdığımız dosyalar; yol geçişi olmaz
        foreach (var dir in new[] { await ResolveDirAsync(), paths.BackupsDir })
        {
            var path = Path.Combine(dir, name);
            if (File.Exists(path)) return path;
        }
        return null;
    }

    private void Prune(string dir, DateTime nowUtc)
    {
        foreach (var b in List(dir).Where(b => b.CreatedAt < nowUtc.AddDays(-KeepDays)))
        {
            try { File.Delete(Path.Combine(dir, b.FileName)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.LogWarning(ex, "Eski yedek silinemedi: {File}", b.FileName); }
        }
        // Çökme ya da elektrik kesintisiyle kalmış yarım yedekler.
        foreach (var t in Directory.EnumerateFiles(dir, "firin-*.tmp*").Where(t => TmpPattern().IsMatch(Path.GetFileName(t))))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(t) < nowUtc.AddHours(-1)) File.Delete(t);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log.LogWarning(ex, "Yarım yedek silinemedi: {File}", t); }
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
