using System.Net;
using System.Net.Http.Json;
using Firin.Api.Domain;
using Firin.Api.Orders;
using Firin.Api.Data;
using Firin.Api.Reports;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;
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
    [InlineData("?from=9999-12-01&to=9999-12-31")]
    [InlineData("?from=0001-01-01&to=0001-01-02")]
    [InlineData("?from=2026-10-01&to=9999-12-31")]
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

        var result = await (await _owner.GetAsync("/api/reports/orders")).ReadAs<OrderHistoryResultDto>();
        Assert.False(result.HasMore);
        var list = result.Orders;
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

    [Fact]
    public async Task Voided_line_in_closed_order_is_excluded_everywhere()
    {
        await SetupAsync();
        var d = await Add(_t1, _tea, 2);
        await Add(_t1, _cookie, 250);
        await _staff.DeleteAsync($"/api/items/{d.Order!.Items[0].Id}");
        await Pay(_t1, "Cash", 15000);

        var s = await Summary();
        Assert.Equal(1, s.ClosedOrders);
        Assert.Equal(15000, s.AverageOrderKurus);
        Assert.Equal("Tuzlu kurabiye", Assert.Single(s.Products).ProductName);

        var h = (await (await _owner.GetAsync("/api/reports/orders")).ReadAs<OrderHistoryResultDto>()).Orders;
        var o = Assert.Single(h);
        Assert.Equal(1, o.ItemCount);
        Assert.Equal(15000, o.TotalKurus);
    }

    [Fact]
    public async Task Revenue_and_products_can_fall_on_different_days()
    {
        await SetupAsync();
        await Add(_t1, _tea, 2);                       // 30 ₺
        await Pay(_t1, "Cash", 1000);                  // 21 Ekim
        _app.Clock.SetUtcNow(new DateTimeOffset(2026, 10, 22, 6, 0, 0, TimeSpan.Zero));
        await Pay(_t1, "Card", 2000);                  // 22 Ekim, hesap kapanır

        var d21 = await Summary("?from=2026-10-21&to=2026-10-21");
        Assert.Equal(1000, d21.RevenueKurus);
        Assert.Equal(0, d21.ClosedOrders);
        Assert.Empty(d21.Products);

        var d22 = await Summary("?from=2026-10-22&to=2026-10-22");
        Assert.Equal(2000, d22.RevenueKurus);
        Assert.Equal(1, d22.ClosedOrders);
        Assert.Equal(2, Assert.Single(d22.Products).Quantity);
    }

    [Fact]
    public async Task Range_of_366_days_is_allowed_and_367_is_not()
    {
        await SetupAsync();
        var ok = await _owner.GetAsync("/api/reports/summary?from=2025-10-21&to=2026-10-21");
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Equal(366, (await ok.ReadAs<SummaryDto>()).Days.Count);
        var bad = await _owner.GetAsync("/api/reports/summary?from=2025-10-20&to=2026-10-21");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Equal("tarih_gecersiz", await bad.ProblemCode());
    }

    [Theory]
    [InlineData("/api/reports/summary")]
    [InlineData("/api/reports/orders")]
    [InlineData("/api/reports/orders/1")]
    public async Task Anonymous_gets_401(string url)
    {
        await SetupAsync();
        Assert.Equal(HttpStatusCode.Unauthorized, (await _app.Client.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task Products_are_grouped_per_product_and_latest_name_wins()
    {
        await SetupAsync();
        await Add(_t1, _tea, 1);
        _app.Clock.Advance(TimeSpan.FromMinutes(5));
        var upd = await _owner.PutAsJsonAsync($"/api/admin/products/{_tea.Id}",
            new { categoryId = _tea.CategoryId, name = "Demli çay", unitType = "Piece", priceKurus = 1500, isActive = true, sortOrder = 0 });
        upd.EnsureSuccessStatusCode();
        await Add(_t2, _tea, 1); // aynı masada aynı ürün tek satırda birleşir; ayrı hesap
        await Add(_t1, _cookie, 100);
        await Pay(_t1, "Cash", 1500 + 6000);
        await Pay(_t2, "Cash", 1500);

        var s = await Summary();
        Assert.Equal(2, s.Products.Count);
        var tea = s.Products.Single(p => p.ProductId == _tea.Id);
        Assert.Equal("Demli çay", tea.ProductName);
        Assert.Equal(2, tea.Quantity);
        Assert.Equal(3000, tea.TotalKurus);
    }

    [Fact]
    public async Task History_is_capped_at_200_with_has_more()
    {
        await SetupAsync();
        var when = new DateTime(2026, 10, 21, 7, 0, 0, DateTimeKind.Utc);
        _app.Db(db =>
        {
            for (var n = 0; n < 205; n++)
                db.Orders.Add(new Order
                {
                    TableId = _t1.Id, Status = OrderStatus.Closed, OpenedAt = when, ClosedAt = when.AddSeconds(n), OpenedById = 1,
                });
            db.SaveChanges();
            return 0;
        });

        var r = await (await _owner.GetAsync("/api/reports/orders")).ReadAs<OrderHistoryResultDto>();
        Assert.Equal(200, r.Orders.Count);
        Assert.True(r.HasMore);
        Assert.Equal(when.AddSeconds(204), r.Orders[0].ClosedAt);
        Assert.Equal(205, (await Summary()).ClosedOrders); // özet sınırdan etkilenmez
    }

    [Fact]
    public async Task Summary_read_transaction_does_not_take_the_write_lock()
    {
        // Özet çalışırken (ikinci sorguda) ayrı bir bağlantı yazma kilidi (BEGIN IMMEDIATE) alabilmeli.
        // WAL kapalıyken okuyucunun SHARED kilidi yazıcının COMMIT'ini yine bekletir (Part 6'da WAL açılır);
        // burada yalnızca RESERVED kilidinin alınmadığı sınanır.
        var probe = new LockProbe();
        using var app = new TestApp(s => s.ConfigureDbContext<AppDb>(o => o.AddInterceptors(probe)));
        var owner = await app.OwnerAsync();
        probe.Arm(app.Db(db => db.Database.GetConnectionString()!));

        var res = await owner.GetAsync("/api/reports/summary");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.True(probe.Fired, "özet sorgusuna araya girilemedi");
        Assert.Null(probe.Error);
    }

    private sealed class LockProbe : DbCommandInterceptor
    {
        private string? _cs;
        private int _seen;
        public bool Fired { get; private set; }
        public string? Error { get; private set; }
        public void Arm(string cs) => _cs = cs;

        public override InterceptionResult<DbDataReader> ReaderExecuting(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result)
        {
            Probe();
            return result;
        }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Probe();
            return ValueTask.FromResult(result);
        }

        private void Probe()
        {
            if (_cs is null || ++_seen != 2) return; // ilk sorgu bitti, işlem açık
            Fired = true;
            try
            {
                using var c = new SqliteConnection(_cs + ";Pooling=False;Default Timeout=1");
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "BEGIN IMMEDIATE; ROLLBACK;";
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException e) { Error = e.Message; }
        }
    }
}
