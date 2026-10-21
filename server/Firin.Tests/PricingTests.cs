using Firin.Api;
using Firin.Api.Domain;
using Firin.Api.Orders;

namespace Firin.Tests;

public class PricingTests
{
    [Theory]
    [InlineData(UnitType.Piece, 2500, 3, 7500)]
    [InlineData(UnitType.Gram, 60000, 250, 15000)]   // 600 ₺/kg × 250 g = 150 ₺
    [InlineData(UnitType.Gram, 45000, 333, 14985)]   // 149,85 ₺
    [InlineData(UnitType.Gram, 1234, 125, 154)]      // 154,25 → 154
    [InlineData(UnitType.Gram, 1000, 500, 500)]
    [InlineData(UnitType.Gram, 1001, 500, 501)]      // 500,5 → 501 (yarım yukarı)
    [InlineData(UnitType.Gram, 99_999, 99_999, 9_999_800)]
    public void LineTotal_is_computed_in_kurus(UnitType unit, long price, int qty, long expected)
    {
        Assert.Equal(expected, Pricing.LineTotal(unit, price, qty));
    }

    [Theory]
    [InlineData(UnitType.Piece, 0)]
    [InlineData(UnitType.Piece, 1000)]
    [InlineData(UnitType.Gram, 0)]
    [InlineData(UnitType.Gram, 100_000)]
    [InlineData(UnitType.Gram, -5)]
    public void Invalid_quantity_throws(UnitType unit, int qty)
    {
        var ex = Assert.Throws<DomainException>(() => Pricing.ValidateQuantity(unit, qty));
        Assert.Equal("miktar_gecersiz", ex.Code);
    }

    [Fact]
    public void OrderMath_ignores_voided_items()
    {
        var order = new Order
        {
            Items =
            [
                new OrderItem { ProductName = "Çay", LineTotalKurus = 3000 },
                new OrderItem { ProductName = "Simit", LineTotalKurus = 2000, VoidedAt = DateTime.UtcNow },
                new OrderItem { ProductName = "Kurabiye", LineTotalKurus = 15000 },
            ],
            Payments = [new Payment { AmountKurus = 5000 }],
        };
        Assert.Equal(18000, OrderMath.Total(order));
        Assert.Equal(5000, OrderMath.Paid(order));
        Assert.Equal(13000, OrderMath.Remaining(order));
    }
}
