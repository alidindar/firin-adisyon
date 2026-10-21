using System.Net;
using System.Net.Http.Json;
using Firin.Api.Domain;
using Firin.Api.Orders;
using Firin.Api.Realtime;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using static Firin.Api.Endpoints.CatalogEndpoints;
using static Firin.Api.Endpoints.TableAdminEndpoints;

namespace Firin.Tests;

public sealed class PaymentTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    private HttpClient _owner = null!;
    private HttpClient _staff = null!;
    private TableDto _table = null!;
    private ProductDto _tea = null!;     // 15 ₺ / adet
    private ProductDto _cookie = null!;  // 600 ₺ / kg

    /// <summary>Masa 1: 2 çay (30 ₺) + 250 g kurabiye (150 ₺) = 180 ₺</summary>
    private async Task<TableDetailDto> SetupAsync()
    {
        var owner = _owner = await _app.OwnerAsync();
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

        // Önce ödeme: tam ödeme adisyonu kapatır, iptal kapalı hesaba takılır (hesap_kapali).
        // Önce iptal: toplam 150 ₺'ye düşer, 180 ₺'lik ödeme kalanı aşar (tutar_fazla).
        var (status, voidedAt) = _app.Db(db => (db.Orders.Single().Status,
            db.OrderItems.Single(i => i.Id == teaLine.Id).VoidedAt));
        if (pay.StatusCode == HttpStatusCode.OK)
        {
            Assert.Equal(HttpStatusCode.Conflict, voided.StatusCode);
            Assert.Equal("hesap_kapali", await voided.ProblemCode());
            Assert.Equal(OrderStatus.Closed, status);
            Assert.Null(voidedAt);
        }
        else
        {
            Assert.Equal(HttpStatusCode.OK, voided.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, pay.StatusCode);
            Assert.Equal("tutar_fazla", await pay.ProblemCode());
            Assert.Equal(OrderStatus.Open, status);
            Assert.NotNull(voidedAt);
            Assert.Equal(0, _app.Db(db => db.Payments.Count()));
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
    public async Task Zero_kurus_line_cannot_be_paid_by_item()
    {
        await SetupAsync();
        var cat = await Seed.CategoryAsync(_owner, "Ucuz");
        var cheap = await Seed.ProductAsync(_owner, cat.Id, "Tuz", UnitType.Gram, 400); // 1 g = 0,4 kuruş -> 0
        var d = await (await _staff.PostAsJsonAsync($"/api/tables/{_table.Id}/items", new { productId = cheap.Id, quantity = 1 })).ReadAs<TableDetailDto>();
        var line = d.Order!.Items.Single(i => i.ProductId == cheap.Id);
        Assert.Equal(0, line.LineTotalKurus);

        Assert.Equal("tutar_gecersiz", await (await Pay(new { method = "Cash", itemIds = new[] { line.Id } })).ProblemCode());
        Assert.Equal(0, _app.Db(db => db.Payments.Count()));
        Assert.Null(_app.Db(db => db.OrderItems.Single(i => i.Id == line.Id).PaymentId));
    }

    [Fact]
    public async Task Voided_item_is_rejected()
    {
        var d = await SetupAsync();
        var teaLine = d.Order!.Items.Single(i => i.ProductId == _tea.Id);
        await _staff.DeleteAsync($"/api/items/{teaLine.Id}");
        var res = await Pay(new { method = "Cash", itemIds = new[] { teaLine.Id } });
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("kalem_yok", await res.ProblemCode());
    }

    [Fact]
    public async Task Item_of_another_table_is_rejected()
    {
        await SetupAsync();
        var other = await Seed.TableAsync(_owner, "Masa 2");
        var od = await (await _staff.PostAsJsonAsync($"/api/tables/{other.Id}/items", new { productId = _tea.Id, quantity = 1 })).ReadAs<TableDetailDto>();
        Assert.Equal("kalem_yok", await (await Pay(new { method = "Cash", itemIds = new[] { od.Order!.Items[0].Id } })).ProblemCode());
        Assert.Equal(0, _app.Db(db => db.Payments.Count()));
    }

    [Fact]
    public async Task Duplicate_item_ids_are_charged_once()
    {
        var d = await SetupAsync();
        var teaLine = d.Order!.Items.Single(i => i.ProductId == _tea.Id);
        var r = await (await Pay(new { method = "Cash", itemIds = new[] { teaLine.Id, teaLine.Id } })).ReadAs<PayResultDto>();
        Assert.Equal(3000, r.Payment.AmountKurus);
    }

    [Fact]
    public async Task Selecting_every_item_closes_order()
    {
        var d = await SetupAsync();
        var r = await (await Pay(new { method = "Card", itemIds = d.Order!.Items.Select(i => i.Id).ToArray() })).ReadAs<PayResultDto>();
        Assert.Equal(18000, r.Payment.AmountKurus);
        Assert.True(r.Closed);
        Assert.Null(r.Table.Order);
    }

    [Fact]
    public async Task Payment_endpoints_require_a_session()
    {
        var d = await SetupAsync();
        var anon = _app.NewClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"/api/orders/{d.Order!.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anon.PostAsJsonAsync($"/api/tables/{_table.Id}/payments", new { method = "Cash", amountKurus = 100 })).StatusCode);
    }

    [Fact]
    public async Task Payment_waits_for_the_shared_order_lock()
    {
        await SetupAsync();
        var orderLock = _app.Factory.Services.GetRequiredService<OrderLock>();
        Task<HttpResponseMessage> payTask;
        using (await orderLock.AcquireAsync())
        {
            payTask = Pay(new { method = "Cash", amountKurus = 100 });
            await Task.Delay(300);
            Assert.False(payTask.IsCompleted);
        }
        Assert.Equal(HttpStatusCode.OK, (await payTask).StatusCode);
    }

    [Fact]
    public async Task Repeated_payment_with_same_expected_remaining_is_recorded_once()
    {
        await SetupAsync();
        var first = await Pay(new { method = "Cash", amountKurus = 5000, expectedRemainingKurus = 18000 });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await Pay(new { method = "Cash", amountKurus = 5000, expectedRemainingKurus = 18000 });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("hesap_degisti", await second.ProblemCode());
        Assert.Equal(1, _app.Db(db => db.Payments.Count()));
    }

    [Fact]
    public async Task Stale_expected_remaining_is_rejected_without_writing()
    {
        await SetupAsync();
        var res = await Pay(new { method = "Card", amountKurus = 5000, expectedRemainingKurus = 17000 });
        Assert.Equal(HttpStatusCode.Conflict, res.StatusCode);
        Assert.Equal("hesap_degisti", await res.ProblemCode());
        Assert.Equal(0, _app.Db(db => db.Payments.Count()));
    }

    [Fact]
    public async Task Matching_expected_remaining_is_accepted()
    {
        await SetupAsync();
        var r = await (await Pay(new { method = "Cash", amountKurus = 18000, expectedRemainingKurus = 18000 })).ReadAs<PayResultDto>();
        Assert.True(r.Closed);
    }

    [Fact]
    public async Task Payment_broadcast_failure_still_returns_ok()
    {
        await SetupAsync();
        var id = _app.SeedUser("Kasa", "1111", Role.Staff);
        using var factory = _app.Factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
        {
            s.RemoveAll<IRealtime>();
            s.AddSingleton<IRealtime, RealtimeTests.ThrowingRealtime>();
        }));
        using var client = factory.CreateClient();
        (await client.PostAsJsonAsync("/api/auth/login", new { userId = id, pin = "1111" })).EnsureSuccessStatusCode();

        var res = await Pay(new { method = "Cash", amountKurus = 18000 }, client);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True((await res.ReadAs<PayResultDto>()).Closed);
        Assert.Equal(1, _app.Db(db => db.Payments.Count()));
        Assert.Equal(OrderStatus.Closed, _app.Db(db => db.Orders.Single().Status));
    }

    [Fact]
    public async Task Payment_is_broadcast()
    {
        await SetupAsync();
        var id = _app.SeedUser("Kasa", "1111", Role.Staff);
        await using var hub = await _app.HubAsync(await _app.LoginCookieAsync(id, "1111"));
        var got = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<int>("TableChanged", t => got.TrySetResult(t));

        Assert.Equal(HttpStatusCode.OK, (await Pay(new { method = "Cash", amountKurus = 5000 })).StatusCode);
        Assert.Equal(_table.Id, await got.Task.WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task Unknown_order_is_404()
    {
        await SetupAsync();
        var res = await _staff.GetAsync("/api/orders/999");
        Assert.Equal("adisyon_yok", await res.ProblemCode());
    }
}
