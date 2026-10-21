using System.Globalization;
using System.Linq.Expressions;
using Firin.Api.Data;
using Firin.Api.Domain;
using Firin.Api.Orders;
using Microsoft.EntityFrameworkCore;

namespace Firin.Api.Reports;

/// <summary>Salt okunur: hiçbir yol yazmaz, bu yüzden OrderLock almaz. AddItemAsync, SetQuantityAsync ya da VoidItemAsync çağırmamalı.</summary>
public sealed class ReportService(AppDb db, OrderService orders, TimeProvider clock, IConfiguration config)
{
    public const int MaxDays = 366;

    private readonly TimeZoneInfo _tz = FindZone(config["Firin:TimeZone"] is { Length: > 0 } z ? z : "Europe/Istanbul");

    private static TimeZoneInfo FindZone(string id)
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) when (id == "Europe/Istanbul")
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");
        }
    }

    public DateOnly Today => LocalDay(clock.GetUtcNow().UtcDateTime);

    private DateOnly LocalDay(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, _tz));

    private DateTime StartOfDayUtc(DateOnly day) =>
        TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), _tz);

    /// <summary>yyyy-MM-dd; boşsa null. Geçersizse tarih_gecersiz.</summary>
    public static DateOnly? ParseDay(string? s)
    {
        if (string.IsNullOrEmpty(s)) return null;
        if (DateOnly.TryParseExact(s, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)) return d;
        throw new DomainException("tarih_gecersiz", "Tarih yyyy-MM-dd biçiminde olmalı.");
    }

    /// <summary>Uçlar dahil yerel gün aralığı; verilmeyen uç diğerine, ikisi de yoksa bugüne eşitlenir.</summary>
    public (DateOnly From, DateOnly To) ResolveRange(string? from, string? to)
    {
        var f = ParseDay(from);
        var t = ParseDay(to);
        var start = f ?? t ?? Today;
        return (start, t ?? start);
    }

    private (DateTime Start, DateTime End) Range(DateOnly from, DateOnly to)
    {
        if (from > to || to.DayNumber - from.DayNumber + 1 > MaxDays)
            throw new DomainException("tarih_gecersiz", $"Tarih aralığı geçersiz (en fazla {MaxDays} gün).");
        return (StartOfDayUtc(from), StartOfDayUtc(to.AddDays(1)));
    }

    public async Task<SummaryDto> SummaryAsync(DateOnly from, DateOnly to)
    {
        var (s, e) = Range(from, to);

        // Tek okuma işlemi: ödemeler ve kapanan adisyonlar aynı anlık görüntüden gelir.
        await using var tx = await db.Database.BeginTransactionAsync();
        var payments = await db.Payments.AsNoTracking().Where(p => p.PaidAt >= s && p.PaidAt < e).ToListAsync();
        var closed = await db.Orders.AsNoTracking().Include(o => o.Items)
            .Where(o => o.Status == OrderStatus.Closed && o.ClosedAt >= s && o.ClosedAt < e).ToListAsync();
        var cancelled = await db.Orders.CountAsync(o => o.Status == OrderStatus.Cancelled && o.ClosedAt >= s && o.ClosedAt < e);
        var voids = (await VoidsAsync(i => i.VoidedAt >= s && i.VoidedAt < e))
            .OrderByDescending(v => v.VoidedAt).ToList();
        await tx.CommitAsync();

        var products = closed.SelectMany(o => o.Items.Where(i => i.VoidedAt == null))
            .GroupBy(i => new { i.ProductId, i.UnitType })
            .Select(g => new ProductSalesDto(g.Key.ProductId,
                g.OrderByDescending(i => i.AddedAt).First().ProductName, g.Key.UnitType,
                g.Sum(i => (long)i.Quantity), g.Sum(i => i.LineTotalKurus)))
            .OrderByDescending(p => p.TotalKurus).ThenBy(p => p.ProductName)
            .ToList();

        long Sum(IEnumerable<Payment> ps, PaymentMethod m) => ps.Where(p => p.Method == m).Sum(p => p.AmountKurus);
        var byDay = payments.ToLookup(p => LocalDay(p.PaidAt));
        var days = Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
            .Select(n => from.AddDays(n))
            .Select(d =>
            {
                var cash = Sum(byDay[d], PaymentMethod.Cash);
                var card = Sum(byDay[d], PaymentMethod.Card);
                return new DailyDto(d, cash + card, cash, card);
            })
            .ToList();

        var cashTotal = Sum(payments, PaymentMethod.Cash);
        var cardTotal = Sum(payments, PaymentMethod.Card);
        var average = closed.Count == 0 ? 0 : closed.Sum(OrderMath.Total) / closed.Count;

        return new SummaryDto(from, to, cashTotal + cardTotal, cashTotal, cardTotal,
            closed.Count, cancelled, average, products, voids, days);
    }

    public async Task<List<OrderHistoryDto>> OrdersAsync(DateOnly from, DateOnly to)
    {
        var (s, e) = Range(from, to);
        return await db.Orders.AsNoTracking()
            .Where(o => o.Status != OrderStatus.Open && o.ClosedAt >= s && o.ClosedAt < e)
            .OrderByDescending(o => o.ClosedAt).ThenByDescending(o => o.Id)
            .Select(o => new OrderHistoryDto(o.Id, o.Table!.Name, o.Status, o.OpenedAt, o.ClosedAt,
                o.Items.Where(i => i.VoidedAt == null).Sum(i => i.LineTotalKurus),
                o.Payments.Sum(p => p.AmountKurus),
                o.Items.Count(i => i.VoidedAt == null)))
            .ToListAsync();
    }

    public async Task<OrderAuditDto> OrderAuditAsync(int orderId)
    {
        var order = await orders.GetOrderAsync(orderId); // yoksa 404 adisyon_yok
        var tableName = await db.Orders.Where(o => o.Id == orderId).Select(o => o.Table!.Name).SingleAsync();
        var voids = await VoidsAsync(i => i.OrderId == orderId);
        return new OrderAuditDto(order, tableName, voids);
    }

    /// <summary>Filtre entity üzerinde uygulanır; constructor'la kurulan DTO üzerinden EF filtre çeviremez.</summary>
    private Task<List<VoidDto>> VoidsAsync(Expression<Func<OrderItem, bool>> filter) =>
        (from i in db.OrderItems.AsNoTracking().Where(i => i.VoidedAt != null).Where(filter)
         join o in db.Orders on i.OrderId equals o.Id
         join t in db.Tables on o.TableId equals t.Id
         join u in db.Users on i.VoidedById equals (int?)u.Id
         orderby i.VoidedAt
         select new VoidDto(o.Id, i.VoidedAt!.Value, t.Name, i.ProductName, i.UnitType, i.Quantity, i.LineTotalKurus, u.Name))
        .ToListAsync();
}
