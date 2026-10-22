using Firin.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Firin.Api.Backups;

/// <summary>Hiçbir ürünün kullanmadığı fotoğraf dosyalarını siler (yarım kalmış yüklemeler, elle silinen ürünler).</summary>
public sealed class PhotoCleanup(FirinPaths paths, IServiceScopeFactory scopes, TimeProvider clock, ILogger<PhotoCleanup> log)
{
    /// <summary>Yeni yüklenmiş ama SaveChanges'i henüz bitmemiş dosyayla yarışmamak için bu kadar yaşlı olmalı.</summary>
    public static readonly TimeSpan MinAge = TimeSpan.FromHours(1);

    public async Task<int> SweepAsync()
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDb>();
        var used = (await db.Products.Where(p => p.PhotoFile != null).Select(p => p.PhotoFile!).ToListAsync()).ToHashSet();
        var cutoff = clock.GetUtcNow().UtcDateTime - MinAge;
        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(paths.PhotosDir))
        {
            if (used.Contains(Path.GetFileName(file)) || File.GetLastWriteTimeUtc(file) > cutoff) continue;
            try { File.Delete(file); removed++; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.LogWarning(ex, "Sahipsiz fotoğraf silinemedi: {File}", file);
            }
        }
        return removed;
    }
}
