using Firin.Api.Data;
using Firin.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Firin.Tests;

public sealed class DataModelTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    private AppDb NewDb() => _app.Factory.Services.CreateScope().ServiceProvider.GetRequiredService<AppDb>();

    [Fact]
    public async Task Product_round_trips_with_enum_and_category()
    {
        await using (var db = NewDb())
        {
            var cat = new Category { Name = "Kuru pasta", SortOrder = 1 };
            db.Categories.Add(cat);
            db.Products.Add(new Product { Category = cat, Name = "Tuzlu kurabiye", UnitType = UnitType.Gram, PriceKurus = 60000 });
            await db.SaveChangesAsync();
        }
        await using (var db = NewDb())
        {
            var p = await db.Products.Include(x => x.Category).SingleAsync();
            Assert.Equal(UnitType.Gram, p.UnitType);
            Assert.Equal(60000, p.PriceKurus);
            Assert.Equal("Kuru pasta", p.Category!.Name);
            Assert.True(p.IsActive);
        }
    }

    [Fact]
    public async Task DateTimes_come_back_as_utc()
    {
        await using (var db = NewDb())
        {
            db.Users.Add(new User { Name = "Ayşe", PinHash = "x", Role = Role.Owner });
            db.Tables.Add(new DiningTable { Name = "Masa 1" });
            await db.SaveChangesAsync();
            db.Orders.Add(new Order { TableId = 1, OpenedById = 1, OpenedAt = new DateTime(2026, 10, 21, 6, 0, 0, DateTimeKind.Utc) });
            await db.SaveChangesAsync();
        }
        await using (var db = NewDb())
        {
            var o = await db.Orders.SingleAsync();
            Assert.Equal(DateTimeKind.Utc, o.OpenedAt.Kind);
            Assert.Equal(OrderStatus.Open, o.Status);
        }
    }

    [Fact]
    public async Task Only_one_open_order_per_table()
    {
        await using var db = NewDb();
        db.Users.Add(new User { Name = "Ayşe", PinHash = "x", Role = Role.Owner });
        db.Tables.Add(new DiningTable { Name = "Masa 1" });
        await db.SaveChangesAsync();
        var now = new DateTime(2026, 10, 21, 6, 0, 0, DateTimeKind.Utc);
        db.Orders.Add(new Order { TableId = 1, OpenedById = 1, OpenedAt = now });
        db.Orders.Add(new Order { TableId = 1, OpenedById = 1, OpenedAt = now, Status = OrderStatus.Closed });
        await db.SaveChangesAsync(); // bir açık + bir kapalı sorun değil
        db.Orders.Add(new Order { TableId = 1, OpenedById = 1, OpenedAt = now });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }
}
