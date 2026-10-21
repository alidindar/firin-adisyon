using System.Net;
using System.Net.Http.Json;
using Firin.Api.Domain;
using Firin.Api.Orders;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.EntityFrameworkCore;
using static Firin.Api.Endpoints.CatalogEndpoints;
using static Firin.Api.Endpoints.TableAdminEndpoints;

namespace Firin.Tests;

public sealed class OrderTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    private HttpClient _owner = null!;
    private HttpClient _staff = null!;
    private TableDto _table = null!;
    private ProductDto _tea = null!;     // 15 ₺ / adet
    private ProductDto _cookie = null!;  // 600 ₺ / kg

    private async Task SetupAsync()
    {
        _owner = await _app.OwnerAsync();
        var cat = await Seed.CategoryAsync(_owner, "Genel");
        _tea = await Seed.ProductAsync(_owner, cat.Id, "Çay", UnitType.Piece, 1500);
        _cookie = await Seed.ProductAsync(_owner, cat.Id, "Tuzlu kurabiye", UnitType.Gram, 60000);
        _table = await Seed.TableAsync(_owner, "Masa 1");
        _staff = await _app.StaffAsync();
    }

    private Task<HttpResponseMessage> Add(int productId, int quantity, int? tableId = null) =>
        _staff.PostAsJsonAsync($"/api/tables/{tableId ?? _table.Id}/items", new { productId, quantity });

    private async Task<TableDetailDto> AddOk(int productId, int quantity)
    {
        var res = await Add(productId, quantity);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return await res.ReadAs<TableDetailDto>();
    }

    private async Task<TableDetailDto> Detail() => await (await _staff.GetAsync($"/api/tables/{_table.Id}")).ReadAs<TableDetailDto>();

    private int SeedPayment(long amount, bool linkAllItems = false) => _app.Db(db =>
    {
        var order = db.Orders.Include(o => o.Items).Single(o => o.TableId == _table.Id && o.Status == OrderStatus.Open);
        var p = new Payment { OrderId = order.Id, AmountKurus = amount, Method = PaymentMethod.Cash, PaidAt = _app.Clock.GetUtcNow().UtcDateTime, UserId = order.OpenedById };
        db.Payments.Add(p);
        db.SaveChanges();
        if (linkAllItems)
        {
            foreach (var i in order.Items) i.PaymentId = p.Id;
            db.SaveChanges();
        }
        return p.Id;
    });

    [Fact]
    public async Task Empty_table_has_no_order()
    {
        await SetupAsync();
        Assert.Null((await Detail()).Order);
        var list = await (await _staff.GetAsync("/api/tables")).ReadAs<List<TableSummaryDto>>();
        var t = Assert.Single(list);
        Assert.Null(t.OrderId);
        Assert.Equal(0, t.TotalKurus);
    }

    [Fact]
    public async Task Adding_items_opens_order_and_computes_total()
    {
        await SetupAsync();
        await AddOk(_tea.Id, 2);
        var d = await AddOk(_cookie.Id, 250);

        Assert.NotNull(d.Order);
        Assert.Equal(3000 + 15000, d.Order.TotalKurus);
        Assert.Equal(18000, d.Order.RemainingKurus);
        Assert.Equal(2, d.Order.Items.Count);
        Assert.Equal("Garson", d.Order.Items[0].AddedBy);

        var t = Assert.Single(await (await _staff.GetAsync("/api/tables")).ReadAs<List<TableSummaryDto>>());
        Assert.Equal(d.Order.Id, t.OrderId);
        Assert.Equal(18000, t.TotalKurus);
        Assert.Equal(2, t.ItemCount);
        Assert.Equal(_app.Clock.GetUtcNow().UtcDateTime, t.OpenedAt!.Value);
    }

    [Fact]
    public async Task Same_piece_product_is_merged_into_one_line()
    {
        await SetupAsync();
        await AddOk(_tea.Id, 1);
        var d = await AddOk(_tea.Id, 2);
        var line = Assert.Single(d.Order!.Items);
        Assert.Equal(3, line.Quantity);
        Assert.Equal(4500, line.LineTotalKurus);
    }

    [Fact]
    public async Task Each_weighing_is_a_separate_line()
    {
        await SetupAsync();
        await AddOk(_cookie.Id, 250);
        var d = await AddOk(_cookie.Id, 300);
        Assert.Equal(new[] { 15000L, 18000L }, d.Order!.Items.Select(i => i.LineTotalKurus).ToArray());
    }

    [Fact]
    public async Task Price_change_does_not_touch_open_order()
    {
        await SetupAsync();
        await AddOk(_tea.Id, 1);
        await _owner.PutAsJsonAsync($"/api/admin/products/{_tea.Id}",
            new { categoryId = _tea.CategoryId, name = "Çay", unitType = "Piece", priceKurus = 2000, isActive = true, sortOrder = 0 });

        Assert.Equal(1500, Assert.Single((await Detail()).Order!.Items).LineTotalKurus);

        var d = await AddOk(_tea.Id, 1); // yeni fiyat yeni satır açar
        Assert.Equal(new[] { 1500L, 2000L }, d.Order!.Items.Select(i => i.UnitPriceKurus).ToArray());
    }

    [Fact]
    public async Task Set_quantity_recomputes_line()
    {
        await SetupAsync();
        var item = Assert.Single((await AddOk(_cookie.Id, 250)).Order!.Items);
        var res = await _staff.PutAsJsonAsync($"/api/items/{item.Id}", new { quantity = 500 });
        var d = await res.ReadAs<TableDetailDto>();
        Assert.Equal(30000, d.Order!.TotalKurus);
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(false, 1000)]
    [InlineData(true, 100_000)]
    public async Task Invalid_quantity_is_rejected(bool gram, int qty)
    {
        await SetupAsync();
        var res = await Add(gram ? _cookie.Id : _tea.Id, qty);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("miktar_gecersiz", await res.ProblemCode());
    }

    [Fact]
    public async Task Merging_beyond_max_pieces_is_rejected()
    {
        await SetupAsync();
        await AddOk(_tea.Id, 998);
        Assert.Equal("miktar_gecersiz", await (await Add(_tea.Id, 2)).ProblemCode());
    }

    [Fact]
    public async Task Inactive_product_and_unknown_table_are_rejected()
    {
        await SetupAsync();
        await _owner.PutAsJsonAsync($"/api/admin/products/{_tea.Id}",
            new { categoryId = _tea.CategoryId, name = "Çay", unitType = "Piece", priceKurus = 1500, isActive = false, sortOrder = 0 });
        Assert.Equal("urun_pasif", await (await Add(_tea.Id, 1)).ProblemCode());

        var res = await Add(_cookie.Id, 100, tableId: 999);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("masa_yok", await res.ProblemCode());
    }

    [Fact]
    public async Task Voiding_last_item_cancels_order_but_keeps_record()
    {
        await SetupAsync();
        var item = Assert.Single((await AddOk(_tea.Id, 1)).Order!.Items);
        var d = await (await _staff.DeleteAsync($"/api/items/{item.Id}")).ReadAs<TableDetailDto>();
        Assert.Null(d.Order);

        var (status, voidedBy) = _app.Db(db =>
        {
            var o = db.Orders.Include(x => x.Items).Single();
            return (o.Status, o.Items.Single().VoidedById);
        });
        Assert.Equal(OrderStatus.Cancelled, status);
        Assert.NotNull(voidedBy);
    }

    [Fact]
    public async Task Voided_item_cannot_be_voided_again()
    {
        await SetupAsync();
        await AddOk(_tea.Id, 1);
        var item = (await AddOk(_cookie.Id, 100)).Order!.Items[1];
        await _staff.DeleteAsync($"/api/items/{item.Id}");
        Assert.Equal("kalem_iptal", await (await _staff.DeleteAsync($"/api/items/{item.Id}")).ProblemCode());
    }

    [Fact]
    public async Task Paid_item_cannot_be_changed_or_voided()
    {
        await SetupAsync();
        await AddOk(_tea.Id, 1);
        var item = Assert.Single((await AddOk(_cookie.Id, 250)).Order!.Items, i => i.ProductId == _cookie.Id);
        await AddOk(_tea.Id, 1); // açık kalsın diye ödenmeyen kalem
        SeedPayment(15000);
        _app.Db(db => { db.OrderItems.Single(i => i.Id == item.Id).PaymentId = db.Payments.Single().Id; return db.SaveChanges(); });

        Assert.Equal("kalem_odendi", await (await _staff.PutAsJsonAsync($"/api/items/{item.Id}", new { quantity = 100 })).ProblemCode());
        Assert.Equal("kalem_odendi", await (await _staff.DeleteAsync($"/api/items/{item.Id}")).ProblemCode());
        Assert.True((await Detail()).Order!.Items.Single(i => i.Id == item.Id).IsPaid);
    }

    [Fact]
    public async Task Total_cannot_drop_below_paid_amount()
    {
        await SetupAsync();
        await AddOk(_tea.Id, 2);                      // 30 ₺
        var d = await AddOk(_cookie.Id, 250);         // 150 ₺
        SeedPayment(16000);
        var cookieLine = d.Order!.Items.Single(i => i.ProductId == _cookie.Id);

        var res = await _staff.DeleteAsync($"/api/items/{cookieLine.Id}");
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("odenenden_az", await res.ProblemCode());
    }

    [Fact]
    public async Task Reaching_paid_amount_closes_order_and_next_item_opens_new_one()
    {
        await SetupAsync();
        await AddOk(_tea.Id, 2);                      // 30 ₺
        var d = await AddOk(_cookie.Id, 250);         // 150 ₺
        var firstOrderId = d.Order!.Id;
        SeedPayment(3000);

        var cookieLine = d.Order.Items.Single(i => i.ProductId == _cookie.Id);
        var after = await (await _staff.DeleteAsync($"/api/items/{cookieLine.Id}")).ReadAs<TableDetailDto>();
        Assert.Null(after.Order);
        Assert.Equal(OrderStatus.Closed, _app.Db(db => db.Orders.Single(o => o.Id == firstOrderId).Status));

        var next = await AddOk(_tea.Id, 1);
        Assert.NotEqual(firstOrderId, next.Order!.Id);
    }

    [Fact]
    public async Task Inactive_tables_are_not_listed()
    {
        await SetupAsync();
        await _owner.PutAsJsonAsync($"/api/admin/tables/{_table.Id}", new { name = "Masa 1", sortOrder = 0, isActive = false });
        Assert.Empty(await (await _staff.GetAsync("/api/tables")).ReadAs<List<TableSummaryDto>>());
        Assert.Equal("masa_pasif", await (await Add(_tea.Id, 1)).ProblemCode());
    }

    [Fact]
    public async Task Anonymous_cannot_read_tables()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.Client.GetAsync("/api/tables")).StatusCode);
    }

    [Fact]
    public async Task Adding_item_broadcasts_table_changed()
    {
        await SetupAsync();
        var id = _app.SeedUser("Kasa", "1111", Role.Staff);
        await using var hub = await _app.HubAsync(await _app.LoginCookieAsync(id, "1111"));
        var got = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<int>("TableChanged", t => got.TrySetResult(t));

        await AddOk(_tea.Id, 1);
        Assert.Equal(_table.Id, await got.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }
}
