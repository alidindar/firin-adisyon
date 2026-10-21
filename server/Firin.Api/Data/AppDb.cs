using Firin.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Firin.Api.Data;

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<DiningTable> Tables => Set<DiningTable>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<Setting> Settings => Set<Setting>();

    protected override void ConfigureConventions(ModelConfigurationBuilder b)
    {
        b.Properties<Role>().HaveConversion<string>();
        b.Properties<UnitType>().HaveConversion<string>();
        b.Properties<OrderStatus>().HaveConversion<string>();
        b.Properties<PaymentMethod>().HaveConversion<string>();
        b.Properties<DateTime>().HaveConversion<UtcConverter>();
        b.Properties<DateTime?>().HaveConversion<NullableUtcConverter>();
    }

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<Setting>().HasKey(s => s.Key);
        m.Entity<Order>()
            .HasIndex(o => o.TableId)
            .HasFilter("\"Status\" = 'Open'")
            .IsUnique();
        m.Entity<Order>().HasIndex(o => o.ClosedAt);
        m.Entity<Product>().HasOne(p => p.Category).WithMany(c => c.Products).OnDelete(DeleteBehavior.Restrict);
        m.Entity<Order>().HasOne(o => o.Table).WithMany().OnDelete(DeleteBehavior.Restrict);
        m.Entity<OrderItem>().HasOne<Payment>().WithMany().HasForeignKey(i => i.PaymentId).OnDelete(DeleteBehavior.Restrict);
    }

    private sealed class UtcConverter() : ValueConverter<DateTime, DateTime>(
        v => v.ToUniversalTime(), v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue ? v.Value.ToUniversalTime() : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
}
