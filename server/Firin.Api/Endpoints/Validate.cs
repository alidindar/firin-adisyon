namespace Firin.Api.Endpoints;

public static class Validate
{
    /// <summary>100.000 ₺</summary>
    public const long MaxPriceKurus = 10_000_000;

    public static string Name(string? value, int max = 60)
    {
        var v = value?.Trim();
        if (string.IsNullOrEmpty(v)) throw new DomainException("ad_gerekli", "Ad boş olamaz.");
        if (v.Length > max) throw new DomainException("ad_uzun", $"Ad en fazla {max} karakter olabilir.");
        return v;
    }

    public static long Price(long kurus) => kurus is > 0 and <= MaxPriceKurus
        ? kurus
        : throw new DomainException("fiyat_gecersiz", "Fiyat 0'dan büyük, en fazla 100.000 ₺ olmalı.");

    public static DomainException NotFound() =>
        new("kayit_yok", "Kayıt bulunamadı.", StatusCodes.Status404NotFound);
}
