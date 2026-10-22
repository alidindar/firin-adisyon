using Firin.Api.Auth;
using Firin.Api.Backups;
using Firin.Api.Settings;

namespace Firin.Api.Endpoints;

public static class SettingsEndpoints
{
    public const string DefaultBusinessName = "Fırın Adisyon";

    public record InfoDto(string BusinessName);
    /// <summary>İkisi de tam değer olarak kaydedilir: null/boş = varsayılana dön.</summary>
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
        await b.GetLastBackupAtAsync(),
        b.LastError,
        NetworkInfo.LanUrls(NetworkInfo.Port(c)));

    private static void EnsureWritable(string dir)
    {
        // Servis LocalSystem olarak çalışır: eşlenmiş sürücü harfleri (Z:) ve çoğu ağ paylaşımı görünmez, klasor_yazilamiyor döner.
        if (!Path.IsPathFullyQualified(dir) || dir.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
            throw new DomainException("yol_gecersiz", @"Yedek klasörü tam yol olmalı, örn. E:\FirinYedek");
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".yazma-testi");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            throw new DomainException("klasor_yazilamiyor", $"Bu klasöre yazılamıyor: {ex.Message}");
        }
    }
}
