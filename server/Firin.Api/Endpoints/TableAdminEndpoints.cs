using Firin.Api.Realtime;
using Firin.Api.Auth;
using Firin.Api.Data;
using Firin.Api.Domain;
using Firin.Api.Orders;
using Microsoft.EntityFrameworkCore;

namespace Firin.Api.Endpoints;

public static class TableAdminEndpoints
{
    public record TableDto(int Id, string Name, int SortOrder, bool IsActive);
    public record TableInput(string Name, int SortOrder, bool? IsActive);

    private static TableDto ToDto(DiningTable t) => new(t.Id, t.Name, t.SortOrder, t.IsActive);

    public static void MapTableAdminEndpoints(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/admin/tables").RequireAuthorization(AuthSetup.OwnerPolicy)
            .NotifiesOnChange(r => r.TablesChanged());

        g.MapGet("/", async (AppDb db) =>
            (await db.Tables.AsNoTracking().OrderBy(t => t.SortOrder).ThenBy(t => t.Name).ToListAsync())
                .Select(ToDto).ToList());

        g.MapPost("/", async (TableInput input, AppDb db) =>
        {
            var isActive = RequireActive(input);
            var t = new DiningTable { Name = await UniqueNameAsync(db, input.Name, null), SortOrder = input.SortOrder, IsActive = isActive };
            db.Tables.Add(t);
            await db.SaveChangesAsync();
            return ToDto(t);
        });

        g.MapPut("/{id:int}", async (int id, TableInput input, AppDb db, OrderLock orderLock) =>
        {
            var t = await db.Tables.FindAsync(id) ?? throw Validate.NotFound();
            var isActive = RequireActive(input);
            t.Name = await UniqueNameAsync(db, input.Name, id);
            t.SortOrder = input.SortOrder;
            // Pasife alırken kontrol+kayıt kilit altında: aynı anda ilk kalem eklenip açık adisyon kalmasın.
            using (await orderLock.AcquireAsync())
            {
                if (t.IsActive && !isActive &&
                    await db.Orders.AnyAsync(o => o.TableId == id && o.Status == OrderStatus.Open))
                    throw new DomainException("masa_acik", "Bu masada açık hesap var. Önce hesabı kapatın.", StatusCodes.Status409Conflict);
                t.IsActive = isActive;
                await db.SaveChangesAsync();
            }
            return ToDto(t);
        });
    }

    private static bool RequireActive(TableInput input) =>
        input.IsActive ?? throw new DomainException("aktiflik_gecersiz", "Aktiflik durumu belirtilmeli.");

    private static async Task<string> UniqueNameAsync(AppDb db, string? raw, int? selfId)
    {
        var name = Validate.Name(raw, 30);
        if (await db.Tables.AnyAsync(t => t.Name == name && t.Id != selfId))
            throw new DomainException("masa_adi_var", "Bu adda bir masa zaten var.", StatusCodes.Status409Conflict);
        return name;
    }
}
