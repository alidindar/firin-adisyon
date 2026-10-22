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

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!config.GetValue("Firin:AutoBackup", true)) return;
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1), clock);
        do
        {
            try
            {
                var latest = backups.List(await backups.ResolveDirAsync()).FirstOrDefault();
                if (IsDue(latest?.CreatedAt, clock.GetUtcNow().UtcDateTime))
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
