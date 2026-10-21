using Firin.Api.Domain;

namespace Firin.Api.Orders;

public static class OrderMath
{
    public static long Total(Order o) => o.Items.Where(i => i.VoidedAt is null).Sum(i => i.LineTotalKurus);
    public static long Paid(Order o) => o.Payments.Sum(p => p.AmountKurus);
    public static long Remaining(Order o) => Total(o) - Paid(o);
}
