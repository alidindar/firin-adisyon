using Firin.Api.Data;
using Firin.Api.Domain;

namespace Firin.Api.Orders;

public sealed class PaymentService(AppDb db, OrderService orders, OrderLock orderLock)
{
    public async Task<PayResultDto> PayAsync(int tableId, PayRequest req, int userId)
    {
        var byAmount = req.AmountKurus is not null;
        var byItems = req.ItemIds is { Count: > 0 };
        if (byAmount == byItems || (req.ItemIds is not null && !byItems))
            throw new DomainException("odeme_gecersiz", "Ya tutar girin ya da ödenecek ürünleri seçin.");
        if (req.Method is not { } method || !Enum.IsDefined(method))
            throw new DomainException("odeme_turu_gecersiz", "Ödeme türü geçersiz.");

        Order order;
        Payment payment;
        // Ortak OrderLock: ödeme, miktar değiştirme ve iptal ile birlikte sıralanır. Kilit tutulurken
        // OrderService'in kilit alan metotları (AddItem/SetQuantity/VoidItem) çağrılmaz.
        using (await orderLock.AcquireAsync())
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            order = await orders.LoadOpenOrderAsync(tableId)
                ?? throw new DomainException("hesap_yok", "Bu masada açık hesap yok.", StatusCodes.Status404NotFound);

            List<OrderItem> items = [];
            long amount;
            if (byItems)
            {
                var ids = req.ItemIds!.Distinct().ToList();
                items = order.Items.Where(i => ids.Contains(i.Id) && i.VoidedAt == null).ToList();
                if (items.Count != ids.Count)
                    throw new DomainException("kalem_yok", "Seçilen kalemlerden biri bu hesapta yok.", StatusCodes.Status404NotFound);
                if (items.Any(i => i.PaymentId is not null))
                    throw new DomainException("kalem_odendi", "Seçilen kalemlerden biri zaten ödenmiş.", StatusCodes.Status409Conflict);
                amount = items.Sum(i => i.LineTotalKurus);
            }
            else
            {
                amount = req.AmountKurus!.Value;
                if (amount <= 0) throw new DomainException("tutar_gecersiz", "Tutar 0'dan büyük olmalı.");
            }

            if (amount > OrderMath.Remaining(order))
                throw new DomainException("tutar_fazla", "Tutar kalan hesaptan fazla.", StatusCodes.Status409Conflict);

            payment = new Payment { OrderId = order.Id, AmountKurus = amount, Method = method, PaidAt = orders.Now, UserId = userId };
            order.Payments.Add(payment);
            await db.SaveChangesAsync();
            foreach (var i in items) i.PaymentId = payment.Id;
            orders.CloseIfSettled(order, userId);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }

        // Kayıt tamamlandı; yayın hatası 500'e dönüşüp istemcinin yeniden ödeme denemesine yol açmamalı.
        await orders.NotifyTableChangedAsync(tableId);
        var userName = (await db.Users.FindAsync(userId))?.Name ?? "?";
        return new PayResultDto(order.Id, order.Status == OrderStatus.Closed,
            new PaymentDto(payment.Id, payment.AmountKurus, payment.Method, payment.PaidAt, userName),
            await orders.GetTableAsync(tableId));
    }
}
