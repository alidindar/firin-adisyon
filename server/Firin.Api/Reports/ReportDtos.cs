using Firin.Api.Domain;
using Firin.Api.Orders;

namespace Firin.Api.Reports;

/// <summary>Quantity: adetli üründe adet, gramlı üründe gram.</summary>
public record ProductSalesDto(int ProductId, string ProductName, UnitType UnitType, long Quantity, long TotalKurus);

public record VoidDto(int OrderId, DateTime VoidedAt, string TableName, string ProductName, UnitType UnitType,
    int Quantity, long LineTotalKurus, string VoidedBy);

public record DailyDto(DateOnly Day, long RevenueKurus, long CashKurus, long CardKurus);

/// <summary>Revenue = aralıkta alınan ödemeler. ClosedOrders, Products ve Average = aralıkta kapanan adisyonlar.</summary>
public record SummaryDto(DateOnly From, DateOnly To, long RevenueKurus, long CashKurus, long CardKurus,
    int ClosedOrders, int CancelledOrders, long AverageOrderKurus,
    List<ProductSalesDto> Products, List<VoidDto> Voids, List<DailyDto> Days);

public record OrderHistoryDto(int Id, string TableName, OrderStatus Status, DateTime OpenedAt, DateTime? ClosedAt,
    long TotalKurus, long PaidKurus, int ItemCount);

public record OrderAuditDto(OrderDto Order, string TableName, List<VoidDto> Voids);
