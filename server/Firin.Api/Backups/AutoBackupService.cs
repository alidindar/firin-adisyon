namespace Firin.Api.Backups;

/// <summary>
/// Saatte bir bakar; son yedek 24 saatten eskiyse yenisini alır ve sahipsiz fotoğrafları temizler.
/// Bilgisayar açılınca da hemen bakar.
/// </summary>
public sealed class AutoBackupService(BackupService backups, PhotoCleanup photos, TimeProvider clock, IConfiguration config, ILogger<AutoBackupService> log)
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
