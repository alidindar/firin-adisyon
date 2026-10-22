namespace Firin.Api.Backups;

/// <summary>
/// Saatte bir bakar; son yedek 24 saatten eskiyse yenisini alır ve sahipsiz fotoğrafları temizler.
/// Bilgisayar açılınca da hemen bakar.
/// </summary>
public sealed class AutoBackupService(BackupService backups, PhotoCleanup photos, TimeProvider clock, IConfiguration config, ILogger<AutoBackupService> log)
    : BackgroundService
{
    /// <summary>Yedek yoksa, 24 saatten eskiyse ya da saat geri alınmış gibi gelecekteyse yeni yedek gerekir.</summary>
    public static bool IsDue(DateTime? latest, DateTime nowUtc)
    {
        if (latest is null) return true;
        var age = nowUtc - latest.Value;
        return age < TimeSpan.Zero || age >= TimeSpan.FromHours(24);
    }

    /// <summary>Gelecek tarihli (saat hatasıyla kalmış) dosyalar "son yedek" sayılmaz.</summary>
    public static BackupDto? Latest(IEnumerable<BackupDto> backups, DateTime nowUtc) =>
        backups.OrderByDescending(b => b.CreatedAt).FirstOrDefault(b => b.CreatedAt <= nowUtc);

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!config.GetValue("Firin:AutoBackup", true)) return;
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), clock);
        do
        {
            try
            {
                var now = clock.GetUtcNow().UtcDateTime;
                var latest = Latest(await backups.ListAsync(), now);
                if (IsDue(latest?.CreatedAt, now))
                    await backups.CreateAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Otomatik yedek başarısız");
            }
            try
            {
                await photos.SweepAsync();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Fotoğraf temizliği başarısız");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }
}
