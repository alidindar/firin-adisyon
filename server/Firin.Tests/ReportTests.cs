using System.Net;
using System.Net.Http.Json;
using Firin.Api.Domain;
using Firin.Api.Orders;
using Firin.Api.Reports;
using static Firin.Api.Endpoints.CatalogEndpoints;
using static Firin.Api.Endpoints.TableAdminEndpoints;

namespace Firin.Tests;

public sealed class ReportTests : IDisposable
{
    private readonly TestApp _app = new();
    public void Dispose() => _app.Dispose();

    private HttpClient _owner = null!;
    private HttpClient _staff = null!;
    private ProductDto _tea = null!;     // 15 ₺ / adet
    private ProductDto _cookie = null!;  // 600 ₺ / kg
    private TableDto _t1 = null!;
    private TableDto _t2 = null!;

    private async Task SetupAsync()
    {
        _owner = await _app.OwnerAsync();
        var cat = await Seed.CategoryAsync(_owner, "Genel");
        _tea = await Seed.ProductAsync(_owner, cat.Id, "Çay", UnitType.Piece, 1500);
        _cookie = await Seed.ProductAsync(_owner, cat.Id, "Tuzlu kurabiye", UnitType.Gram, 60000);
        _t1 = await Seed.TableAsync(_owner, "Masa 1");
        _t2 = await Seed.TableAsync(_owner, "Masa 2");
        _staff = await _app.StaffAsync();
    }

    private async Task<TableDetailDto> Add(TableDto t, ProductDto p, int qty) =>
        await (await _staff.PostAsJsonAsync($"/api/tables/{t.Id}/items", new { productId = p.Id, quantity = qty })).ReadAs<TableDetailDto>();

    private async Task Pay(TableDto t, string method, long amount) =>
        (await _staff.PostAsJsonAsync($"/api/tables/{t.Id}/payments", new { method, amountKurus = amount })).EnsureSuccessStatusCode();

    private async Task<SummaryDto> Summary(string query = "") =>
        await (await _owner.GetAsync($"/api/reports/summary{query}")).ReadAs<SummaryDto>();

    /// <summary>Masa 1: 2 çay + 250 g kurabiye = 180 ₺ (100 nakit + 80 kart). Masa 2: 1 çay eklenip iptal → iptal adisyon.</summary>
    private async Task SeedDayAsync()
    {
        await Add(_t1, _tea, 2);
        await Add(_t1, _cookie, 250);
        await Pay(_t1, "Cash", 10000);
        await Pay(_t1, "Card", 8000);
        var d = await Add(_t2, _tea, 1);
        await _staff.DeleteAsync($"/api/items/{d.Order!.Items[0].Id}");
    }

    [Fact]
    public async Task Summary_of_today()
    {
        await SetupAsync();
        await SeedDayAsync();

        var s = await Summary();
        Assert.Equal(new DateOnly(2026, 10, 21), s.From);
        Assert.Equal(18000, s.RevenueKurus);
        Assert.Equal(10000, s.CashKurus);
        Assert.Equal(8000, s.CardKurus);
        Assert.Equal(1, s.ClosedOrders);
        Assert.Equal(1, s.CancelledOrders);
        Assert.Equal(18000, s.AverageOrderKurus);

        Assert.Equal(new[] { "Tuzlu kurabiye", "Çay" }, s.Products.Select(p => p.ProductName).ToArray());
        Assert.Equal(250, s.Products[0].Quantity);
        Assert.Equal(15000, s.Products[0].TotalKurus);
        Assert.Equal(2, s.Products[1].Quantity);

        var v = Assert.Single(s.Voids);
        Assert.Equal("Masa 2", v.TableName);
        Assert.Equal("Çay", v.ProductName);
        Assert.Equal("Garson", v.VoidedBy);

        var day = Assert.Single(s.Days);
        Assert.Equal(18000, day.RevenueKurus);
    }

    [Fact]
    public async Task Day_boundary_follows_istanbul_time()
    {
        await SetupAsync();
        _app.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 21, 20, 59, 0, TimeSpan.Zero)); // 23:59 İstanbul
        await Add(_t1, _tea, 1);
        await Pay(_t1, "Cash", 1500);
        _app.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 21, 21, 0, 0, TimeSpan.Zero));  // 22 Ekim 00:00 İstanbul
        await Add(_t1, _tea, 2);
        await Pay(_t1, "Card", 3000);

        Assert.Equal(1500, (await Summary("?from=2026-10-21&to=2026-10-21")).RevenueKurus);
        Assert.Equal(3000, (await Summary("?from=2026-10-22&to=2026-10-22")).RevenueKurus);

        var both = await Summary("?from=2026-10-21&to=2026-10-22");
        Assert.Equal(4500, both.RevenueKurus);
        Assert.Equal(new[] { 1500L, 3000L }, both.Days.Select(d => d.RevenueKurus).ToArray());
    }

    [Fact]
    public async Task Days_without_sales_are_zero_filled()
    {
        await SetupAsync();
        var s = await Summary("?from=2026-10-19&to=2026-10-21");
        Assert.Equal(3, s.Days.Count);
        Assert.All(s.Days, d => Assert.Equal(0, d.RevenueKurus));
    }

    [Fact]
    public async Task Partial_payment_on_open_order_counts_as_revenue()
    {
        await SetupAsync();
        await Add(_t1, _tea, 2);
        await Pay(_t1, "Cash", 1000);
        var s = await Summary();
        Assert.Equal(1000, s.RevenueKurus);
        Assert.Equal(0, s.ClosedOrders);
        Assert.Empty(s.Products);
    }

    [Theory]
    [InlineData("?from=2026-10-22&to=2026-10-21")]
    [InlineData("?from=2025-01-01&to=2026-10-21")]
    [InlineData("?from=abc&to=2026-10-21")]
    [InlineData("?from=2026-13-01&to=2026-10-21")]
    public async Task Invalid_range_is_rejected(string query)
    {
        await SetupAsync();
        var res = await _owner.GetAsync($"/api/reports/summary{query}");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Equal("tarih_gecersiz", await res.ProblemCode());
    }

    [Fact]
    public async Task Staff_cannot_see_reports()
    {
        await SetupAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await _staff.GetAsync("/api/reports/summary")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _staff.GetAsync("/api/reports/orders")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await _staff.GetAsync("/api/reports/orders/1")).StatusCode);
    }

    [Fact]
    public async Task Unknown_order_audit_is_404()
    {
        await SetupAsync();
        var res = await _owner.GetAsync("/api/reports/orders/9999");
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
        Assert.Equal("adisyon_yok", await res.ProblemCode());
    }

    [Fact]
    public async Task History_lists_closed_and_cancelled_orders_with_audit()
    {
        await SetupAsync();
        await SeedDayAsync();

        var list = await (await _owner.GetAsync("/api/reports/orders")).ReadAs<List<OrderHistoryDto>>();
        Assert.Equal(2, list.Count);
        var closed = list.Single(o => o.Status == OrderStatus.Closed);
        Assert.Equal("Masa 1", closed.TableName);
        Assert.Equal(18000, closed.TotalKurus);
        Assert.Equal(18000, closed.PaidKurus);
        var cancelled = list.Single(o => o.Status == OrderStatus.Cancelled);
        Assert.Equal(0, cancelled.TotalKurus);

        var audit = await (await _owner.GetAsync($"/api/reports/orders/{cancelled.Id}")).ReadAs<OrderAuditDto>();
        Assert.Equal("Masa 2", audit.TableName);
        Assert.Empty(audit.Order.Items);
        Assert.Equal("Çay", Assert.Single(audit.Voids).ProductName);
    }
}
