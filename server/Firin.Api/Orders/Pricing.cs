using Firin.Api.Domain;

namespace Firin.Api.Orders;

public static class Pricing
{
    public const int MaxPieces = 999;
    public const int MaxGrams = 99_999;

    /// <summary>Adetli: adet × adet fiyatı. Gramlı: gram × kg fiyatı / 1000, kuruşa yarım yukarı yuvarlanır.</summary>
    public static long LineTotal(UnitType unit, long unitPriceKurus, int quantity) => unit switch
    {
        UnitType.Piece => unitPriceKurus * quantity,
        UnitType.Gram => (long)Math.Round(quantity * (decimal)unitPriceKurus / 1000m, MidpointRounding.AwayFromZero),
        _ => throw new ArgumentOutOfRangeException(nameof(unit)),
    };

    public static void ValidateQuantity(UnitType unit, int quantity)
    {
        var max = unit == UnitType.Piece ? MaxPieces : MaxGrams;
        if (quantity < 1 || quantity > max)
            throw new DomainException("miktar_gecersiz", unit == UnitType.Piece
                ? $"Adet 1 ile {MaxPieces} arasında olmalı."
                : "Gram 1 ile 99.999 arasında olmalı.");
    }
}
