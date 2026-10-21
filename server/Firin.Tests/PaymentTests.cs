using System.Net;
using System.Net.Http.Json;
using Firin.Api.Domain;
using Firin.Api.Orders;
using static Firin.Api.Endpoints.CatalogEndpoints;
using static Firin.Api.Endpoints.TableAdminEndpoints;

namespace Firin.Tests;

public sealed class PaymentTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    private HttpClient _staff = null!;
    private TableDto _table = null!;
    private ProductDto _tea = null!;     // 15 ₺ / adet
    private ProductDto _cookie = null!;  // 600 ₺ / kg

    /// <summary>Masa 1: 2 çay (30 ₺) + 250 g kurabiye (150 ₺) = 180 ₺</summary>
    private async Task<TableDetailDto> SetupAsync()
    {
        var owner = await _app.OwnerAsync();
        var cat = await Seed.CategoryAsync(owner, "Genel");
        _tea = await Seed.ProductAsync(owner, cat.Id, "Çay", UnitType.Piece, 1500);
        _cookie = await Seed.ProductAsync(owner, cat.Id, "Tuzlu kurabiye", UnitType.Gram, 60000);
        _table = await Seed.TableAsync(owner, "Masa 1");
        _staff = await _app.StaffAsync();
        await _staff.PostAsJsonAsync($"/api/tables/{_table.Id}/items", new { productId = _tea.Id, quantity = 2 });
        var res = await _staff.PostAsJsonAsync($"/api/tables/{_table.Id}/items", new { productId = _cookie.Id, quantity = 250 });
        return await res.ReadAs<TableDetailDto>();
    }

    private Task<HttpResponseMessage> Pay(object body, HttpClient? client = null) =>
        (client ?? _staff).PostAsJsonAsync($"/api/tables/{_table.Id}/payments", body);

    [Fact]
    public async Task Full_payment_closes_order()
    {
        var d = await SetupAsync();
        var res = await Pay(new { method = "Cash", amountKurus = 18000 });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var r = await res.ReadAs<PayResultDto>();
        Assert.True(r.Closed);
        Assert.Equal(d.Order!.Id, r.OrderId);
        Assert.Null(r.Table.Order);
        Assert.Equal(PaymentMethod.Cash, r.Payment.Method);

        var order = await (await _staff.GetAsync($"/api/orders/{r.OrderId}")).ReadAs<OrderDto>();
        Assert.Equal(OrderStatus.Closed, order.Status);
        Assert.Equal(18000, order.PaidKurus);
        Assert.Equal(0, order.RemainingKurus);
        Assert.Equal(2, order.Items.Count);
    }

    [Fact]
    public async Task Partial_payment_keeps_order_open()
    {
        await SetupAsync();
        var r = await (await Pay(new { method = "Card", amountKurus = 5000 })).ReadAs<PayResultDto>();
        Assert.False(r.Closed);
        Assert.Equal(13000, r.Table.Order!.RemainingKurus);
        Assert.Equal(5000, r.Table.Order.PaidKurus);

        var t = Assert.Single(await (await _staff.GetAsync("/api/tables")).ReadAs<List<TableSummaryDto>>());
        Assert.Equal(18000, t.TotalKurus);
        Assert.Equal(13000, t.RemainingKurus);
    }

    [Theory]
    [InlineData(18001, "tutar_fazla")]
    [InlineData(0, "tutar_gecersiz")]
    [InlineData(-100, "tutar_gecersiz")]
    public async Task Invalid_amount_is_rejected(long amount, string code)
    {
        await SetupAsync();
        Assert.Equal(code, await (await Pay(new { method = "Cash", amountKurus = amount })).ProblemCode());
    }

    [Fact]
    public async Task Request_must_have_exactly_one_of_amount_or_items()
    {
        var d = await SetupAsync();
        Assert.Equal("odeme_gecersiz", await (await Pay(new { method = "Cash" })).ProblemCode());
        Assert.Equal("odeme_gecersiz", await (await Pay(new { method = "Cash", amountKurus = 100, itemIds = new[] { d.Order!.Items[0].Id } })).ProblemCode());
        Assert.Equal("odeme_gecersiz", await (await Pay(new { method = "Cash", itemIds = Array.Empty<int>() })).ProblemCode());
    }

    [Fact]
    public async Task Missing_method_is_rejected()
    {
        await SetupAsync();
        Assert.Equal("odeme_turu_gecersiz", await (await Pay(new { amountKurus = 100 })).ProblemCode());
        Assert.Equal(0, _app.Db(db => db.Payments.Count()));
    }

    [Fact]
    public async Task Empty_table_cannot_be_paid()
    {
        await SetupAsync();
        await Pay(new { method = "Cash", amountKurus = 18000 });
        var res = await Pay(new { method = "Cash", amountKurus = 100 });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("hesap_yok", await res.ProblemCode());
    }

    [Fact]
    public async Task Pay_by_items_marks_items_and_charges_their_total()
    {
        var d = await SetupAsync();
        var cookieLine = d.Order!.Items.Single(i => i.ProductId == _cookie.Id);

        var r = await (await Pay(new { method = "Cash", itemIds = new[] { cookieLine.Id } })).ReadAs<PayResultDto>();
        Assert.Equal(15000, r.Payment.AmountKurus);
        Assert.False(r.Closed);
        Assert.True(r.Table.Order!.Items.Single(i => i.Id == cookieLine.Id).IsPaid);
        Assert.Equal(3000, r.Table.Order.RemainingKurus);

        Assert.Equal("kalem_odendi", await (await Pay(new { method = "Cash", itemIds = new[] { cookieLine.Id } })).ProblemCode());

        var last = await (await Pay(new { method = "Card", amountKurus = 3000 })).ReadAs<PayResultDto>();
        Assert.True(last.Closed);
    }

    [Fact]
    public async Task Item_payment_cannot_exceed_remaining()
    {
        var d = await SetupAsync();
        await Pay(new { method = "Cash", amountKurus = 16000 });
        var cookieLine = d.Order!.Items.Single(i => i.ProductId == _cookie.Id);
        Assert.Equal("tutar_fazla", await (await Pay(new { method = "Cash", itemIds = new[] { cookieLine.Id } })).ProblemCode());
    }

    [Fact]
    public async Task Unknown_item_is_rejected()
    {
        await SetupAsync();
        Assert.Equal("kalem_yok", await (await Pay(new { method = "Cash", itemIds = new[] { 9999 } })).ProblemCode());
    }

    [Fact]
    public async Task Methods_are_recorded_per_payment()
    {
        await SetupAsync();
        await Pay(new { method = "Cash", amountKurus = 10000 });
        var r = await (await Pay(new { method = "Card", amountKurus = 8000 })).ReadAs<PayResultDto>();
        var order = await (await _staff.GetAsync($"/api/orders/{r.OrderId}")).ReadAs<OrderDto>();
        Assert.Equal(new[] { PaymentMethod.Cash, PaymentMethod.Card }, order.Payments.Select(p => p.Method).ToArray());
        Assert.Equal("Garson", order.Payments[0].UserName);
    }

    [Fact]
    public async Task Concurrent_full_payments_do_not_overpay()
    {
        await SetupAsync();
        var other = await _app.StaffAsync("Kasa");
        var results = await Task.WhenAll(
            Pay(new { method = "Cash", amountKurus = 18000 }),
            Pay(new { method = "Card", amountKurus = 18000 }, other));

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Equal(18000, _app.Db(db => db.Payments.Sum(p => p.AmountKurus)));
    }

    [Fact]
    public async Task Payment_and_void_are_serialized()
    {
        var d = await SetupAsync();
        var teaLine = d.Order!.Items.Single(i => i.ProductId == _tea.Id);
        var other = await _app.StaffAsync("Kasa");

        var payTask = Pay(new { method = "Cash", amountKurus = 18000 });
        var voidTask = other.DeleteAsync($"/api/items/{teaLine.Id}");
        var (pay, voided) = (await payTask, await voidTask);

        if (pay.StatusCode == HttpStatusCode.OK)
            Assert.Equal("odenenden_az", await voided.ProblemCode());
        else
        {
            Assert.Equal(HttpStatusCode.OK, voided.StatusCode);
            Assert.Equal("tutar_fazla", await pay.ProblemCode());
        }

        var (paid, total) = _app.Db(db => (
            db.Payments.Sum(p => p.AmountKurus),
            db.OrderItems.Where(i => i.VoidedAt == null).Sum(i => i.LineTotalKurus)));
        Assert.True(paid <= total, $"paid {paid} > total {total}");
    }

    [Fact]
    public async Task Put_down_to_paid_amount_closes_order()
    {
        var d = await SetupAsync();
        var teaLine = d.Order!.Items.Single(i => i.ProductId == _tea.Id);
        await Pay(new { method = "Cash", amountKurus = 16500 });

        var res = await _staff.PutAsJsonAsync($"/api/items/{teaLine.Id}", new { quantity = 1 });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Null((await res.ReadAs<TableDetailDto>()).Order);
        Assert.Equal(OrderStatus.Closed, _app.Db(db => db.Orders.Single().Status));
    }

    [Fact]
    public async Task Put_below_paid_amount_is_rejected()
    {
        var d = await SetupAsync();
        var teaLine = d.Order!.Items.Single(i => i.ProductId == _tea.Id);
        await Pay(new { method = "Cash", amountKurus = 17000 });

        var res = await _staff.PutAsJsonAsync($"/api/items/{teaLine.Id}", new { quantity = 1 });
        Assert.Equal("odenenden_az", await res.ProblemCode());
        Assert.Equal(OrderStatus.Open, _app.Db(db => db.Orders.Single().Status));
    }

    [Fact]
    public async Task Closed_order_items_cannot_be_changed()
    {
        var d = await SetupAsync();
        var teaLine = d.Order!.Items.Single(i => i.ProductId == _tea.Id);
        await Pay(new { method = "Cash", amountKurus = 18000 });

        Assert.Equal("hesap_kapali", await (await _staff.PutAsJsonAsync($"/api/items/{teaLine.Id}", new { quantity = 1 })).ProblemCode());
        Assert.Equal("hesap_kapali", await (await _staff.DeleteAsync($"/api/items/{teaLine.Id}")).ProblemCode());
    }

    [Fact]
    public async Task Unknown_order_is_404()
    {
        await SetupAsync();
        var res = await _staff.GetAsync("/api/orders/999");
        Assert.Equal("adisyon_yok", await res.ProblemCode());
    }
}
