using Firin.Api.Domain;

namespace Firin.Api.Orders;

public record OrderItemDto(int Id, int ProductId, string ProductName, UnitType UnitType, long UnitPriceKurus,
    int Quantity, long LineTotalKurus, bool IsPaid, DateTime AddedAt, string AddedBy);

public record PaymentDto(int Id, long AmountKurus, PaymentMethod Method, DateTime PaidAt, string UserName);

public record OrderDto(int Id, OrderStatus Status, DateTime OpenedAt, long TotalKurus, long PaidKurus,
    long RemainingKurus, List<OrderItemDto> Items, List<PaymentDto> Payments);

/// <summary>Order: masanın açık adisyonu; yoksa null.</summary>
public record TableDetailDto(int Id, string Name, OrderDto? Order);

/// <summary>Tutar ya da kalem listesinden tam olarak biri verilir. Method zorunlu (eksikse varsayılan Cash olmasın).
/// ExpectedRemainingKurus verilirse ve güncel kalanla farklıysa ödeme yazılmaz (kaybolan yanıt sonrası tekrar / bayat ekran).</summary>
public record PayRequest(PaymentMethod? Method, long? AmountKurus, List<int>? ItemIds, long? ExpectedRemainingKurus = null);

public record PayResultDto(int OrderId, bool Closed, PaymentDto Payment, TableDetailDto Table);

public record TableSummaryDto(int Id, string Name, int? OrderId, long TotalKurus, long RemainingKurus,
    DateTime? OpenedAt, int ItemCount);
