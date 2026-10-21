using Firin.Api.Data;
using Firin.Api.Domain;
using Firin.Api.Realtime;
using Microsoft.EntityFrameworkCore;

namespace Firin.Api.Orders;

public sealed class OrderService(AppDb db, TimeProvider clock, IRealtime realtime, ILogger<OrderService> log)
{
    public DateTime Now => clock.GetUtcNow().UtcDateTime;

    public async Task<List<TableSummaryDto>> ListTablesAsync()
    {
        var tables = await db.Tables.AsNoTracking().Where(t => t.IsActive)
            .OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync();
        var open = await db.Orders.AsNoTracking().Where(o => o.Status == OrderStatus.Open)
            .Select(o => new
            {
                o.Id,
                o.TableId,
                o.OpenedAt,
                Total = o.Items.Where(i => i.VoidedAt == null).Sum(i => i.LineTotalKurus),
                Paid = o.Payments.Sum(p => p.AmountKurus),
                Count = o.Items.Count(i => i.VoidedAt == null),
            })
            .ToListAsync();
        var byTable = open.ToDictionary(o => o.TableId);
        return tables.Select(t => byTable.TryGetValue(t.Id, out var o)
                ? new TableSummaryDto(t.Id, t.Name, o.Id, o.Total, o.Total - o.Paid, o.OpenedAt, o.Count)
                : new TableSummaryDto(t.Id, t.Name, null, 0, 0, null, 0))
            .ToList();
    }

    public async Task<TableDetailDto> GetTableAsync(int tableId)
    {
        var table = await db.Tables.FirstOrDefaultAsync(t => t.Id == tableId) ?? throw TableNotFound();
        var order = await LoadOpenOrderAsync(tableId);
        return new TableDetailDto(table.Id, table.Name, order is null ? null : await ToDtoAsync(order));
    }

    public Task<Order?> LoadOpenOrderAsync(int tableId) => db.Orders
        .Include(o => o.Items).Include(o => o.Payments)
        .FirstOrDefaultAsync(o => o.TableId == tableId && o.Status == OrderStatus.Open);

    public async Task<TableDetailDto> AddItemAsync(int tableId, int productId, int quantity, int userId)
    {
        var table = await db.Tables.FirstOrDefaultAsync(t => t.Id == tableId) ?? throw TableNotFound();
        if (!table.IsActive) throw new DomainException("masa_pasif", "Bu masa kullanımda değil.");
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId)
            ?? throw new DomainException("urun_yok", "Ürün bulunamadı.", StatusCodes.Status404NotFound);
        if (!product.IsActive) throw new DomainException("urun_pasif", "Bu ürün satışta değil.");
        Pricing.ValidateQuantity(product.UnitType, quantity);

        var order = await LoadOpenOrderAsync(tableId) ?? await OpenOrderAsync(tableId, userId);

        var merge = product.UnitType == UnitType.Piece
            ? order.Items.FirstOrDefault(i => i.ProductId == productId && i.VoidedAt == null
                && i.PaymentId == null && i.UnitPriceKurus == product.PriceKurus)
            : null;
        if (merge is not null)
        {
            var q = merge.Quantity + quantity;
            Pricing.ValidateQuantity(UnitType.Piece, q);
            merge.Quantity = q;
            merge.LineTotalKurus = Pricing.LineTotal(UnitType.Piece, merge.UnitPriceKurus, q);
        }
        else
        {
            order.Items.Add(new OrderItem
            {
                ProductId = product.Id,
                ProductName = product.Name,
                UnitType = product.UnitType,
                UnitPriceKurus = product.PriceKurus,
                Quantity = quantity,
                LineTotalKurus = Pricing.LineTotal(product.UnitType, product.PriceKurus, quantity),
                AddedById = userId,
                AddedAt = Now,
            });
        }
        await db.SaveChangesAsync();
        await NotifyAsync(tableId);
        return await GetTableAsync(tableId);
    }

    public async Task<TableDetailDto> SetQuantityAsync(int itemId, int quantity, int userId)
    {
        var (order, item) = await LoadEditableItemAsync(itemId);
        Pricing.ValidateQuantity(item.UnitType, quantity);
        item.Quantity = quantity;
        item.LineTotalKurus = Pricing.LineTotal(item.UnitType, item.UnitPriceKurus, quantity);
        EnsureNotBelowPaid(order);
        CloseIfSettled(order, userId);
        await db.SaveChangesAsync();
        await NotifyAsync(order.TableId);
        return await GetTableAsync(order.TableId);
    }

    public async Task<TableDetailDto> VoidItemAsync(int itemId, int userId)
    {
        var (order, item) = await LoadEditableItemAsync(itemId);
        item.VoidedAt = Now;
        item.VoidedById = userId;
        EnsureNotBelowPaid(order);
        if (!order.Items.Any(i => i.VoidedAt == null) && order.Payments.Count == 0)
        {
            order.Status = OrderStatus.Cancelled;
            order.ClosedAt = Now;
            order.ClosedById = userId;
        }
        else
        {
            CloseIfSettled(order, userId);
        }
        await db.SaveChangesAsync();
        await NotifyAsync(order.TableId);
        return await GetTableAsync(order.TableId);
    }

    /// <summary>Aktif kalem varsa ve kalan sıfırsa adisyonu kapatır. Kaydetmez.</summary>
    public void CloseIfSettled(Order order, int userId)
    {
        if (order.Items.Any(i => i.VoidedAt == null) && OrderMath.Remaining(order) == 0)
        {
            order.Status = OrderStatus.Closed;
            order.ClosedAt = Now;
            order.ClosedById = userId;
        }
    }

    // Kayıt tamamlandıktan sonra yayın hatası istemciye 500 olarak dönmesin (tekrar denemesin).
    private async Task NotifyAsync(int tableId)
    {
        try { await realtime.TableChanged(tableId); }
        catch (Exception e) { log.LogWarning(e, "Masa değişikliği yayını gönderilemedi."); }
    }

    private async Task<Order> OpenOrderAsync(int tableId, int userId)
    {
        var order = new Order { TableId = tableId, OpenedAt = Now, OpenedById = userId };
        db.Orders.Add(order);
        try
        {
            await db.SaveChangesAsync();
            return order;
        }
        catch (DbUpdateException)
        {
            // Başka bir cihaz aynı anda bu masaya adisyon açtı (benzersiz indeks); onunkini kullan.
            db.Entry(order).State = EntityState.Detached;
            var existing = await LoadOpenOrderAsync(tableId);
            if (existing is null) throw;
            return existing;
        }
    }

    private async Task<(Order, OrderItem)> LoadEditableItemAsync(int itemId)
    {
        var orderId = await db.OrderItems.Where(i => i.Id == itemId).Select(i => (int?)i.OrderId).FirstOrDefaultAsync()
            ?? throw new DomainException("kalem_yok", "Kalem bulunamadı.", StatusCodes.Status404NotFound);
        var order = await db.Orders.Include(o => o.Items).Include(o => o.Payments).FirstAsync(o => o.Id == orderId);
        if (order.Status != OrderStatus.Open)
            throw new DomainException("hesap_kapali", "Bu hesap kapanmış.", StatusCodes.Status409Conflict);
        var item = order.Items.Single(i => i.Id == itemId);
        if (item.VoidedAt is not null)
            throw new DomainException("kalem_iptal", "Bu kalem zaten iptal edilmiş.", StatusCodes.Status409Conflict);
        if (item.PaymentId is not null)
            throw new DomainException("kalem_odendi", "Ödemesi alınmış kalem değiştirilemez.", StatusCodes.Status409Conflict);
        return (order, item);
    }

    private static void EnsureNotBelowPaid(Order order)
    {
        if (OrderMath.Total(order) < OrderMath.Paid(order))
            throw new DomainException("odenenden_az",
                "Bu hesaptan ödeme alındı; toplam, ödenen tutarın altına düşemez.", StatusCodes.Status409Conflict);
    }

    private async Task<OrderDto> ToDtoAsync(Order o)
    {
        var userIds = o.Items.Select(i => i.AddedById).Concat(o.Payments.Select(p => p.UserId)).Distinct().ToList();
        var names = await db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name);
        string Name(int id) => names.GetValueOrDefault(id, "?");

        return new OrderDto(o.Id, o.Status, o.OpenedAt, OrderMath.Total(o), OrderMath.Paid(o), OrderMath.Remaining(o),
            o.Items.Where(i => i.VoidedAt == null).OrderBy(i => i.AddedAt).ThenBy(i => i.Id)
                .Select(i => new OrderItemDto(i.Id, i.ProductId, i.ProductName, i.UnitType, i.UnitPriceKurus,
                    i.Quantity, i.LineTotalKurus, i.PaymentId != null, i.AddedAt, Name(i.AddedById)))
                .ToList(),
            o.Payments.OrderBy(p => p.PaidAt).ThenBy(p => p.Id)
                .Select(p => new PaymentDto(p.Id, p.AmountKurus, p.Method, p.PaidAt, Name(p.UserId)))
                .ToList());
    }

    private static DomainException TableNotFound() =>
        new("masa_yok", "Masa bulunamadı.", StatusCodes.Status404NotFound);
}
