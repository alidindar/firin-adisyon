namespace Firin.Api.Domain;

public enum Role { Owner, Staff }
public enum UnitType { Piece, Gram }
public enum OrderStatus { Open, Closed, Cancelled }
public enum PaymentMethod { Cash, Card }

public class User
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string PinHash { get; set; }
    public Role Role { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Category
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public List<Product> Products { get; set; } = [];
}

public class Product
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public Category? Category { get; set; }
    public required string Name { get; set; }
    public UnitType UnitType { get; set; }
    /// <summary>Adetli üründe adet fiyatı, gramlı üründe kilogram fiyatı (kuruş).</summary>
    public long PriceKurus { get; set; }
    /// <summary>`photos/` altındaki dosya adı.</summary>
    public string? PhotoFile { get; set; }
    public bool IsActive { get; set; } = true;
    public int SortOrder { get; set; }
}

public class DiningTable
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Order
{
    public int Id { get; set; }
    public int TableId { get; set; }
    public DiningTable? Table { get; set; }
    public OrderStatus Status { get; set; } = OrderStatus.Open;
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public int OpenedById { get; set; }
    public int? ClosedById { get; set; }
    public List<OrderItem> Items { get; set; } = [];
    public List<Payment> Payments { get; set; } = [];
}

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public int ProductId { get; set; }
    // Ürünün eklendiği andaki kopyası: fiyat değişse de hesap değişmez.
    public required string ProductName { get; set; }
    public UnitType UnitType { get; set; }
    public long UnitPriceKurus { get; set; }
    /// <summary>Adet ya da gram.</summary>
    public int Quantity { get; set; }
    public long LineTotalKurus { get; set; }
    public int AddedById { get; set; }
    public DateTime AddedAt { get; set; }
    public DateTime? VoidedAt { get; set; }
    public int? VoidedById { get; set; }
    /// <summary>Ürün seçerek ödemede bu kalemi kapatan ödeme.</summary>
    public int? PaymentId { get; set; }
}

public class Payment
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public long AmountKurus { get; set; }
    public PaymentMethod Method { get; set; }
    public DateTime PaidAt { get; set; }
    public int UserId { get; set; }
}

public class Setting
{
    public required string Key { get; set; }
    public required string Value { get; set; }
}
