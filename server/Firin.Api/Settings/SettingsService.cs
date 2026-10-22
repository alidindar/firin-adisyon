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
