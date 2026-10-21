using Firin.Api.Auth;
using Firin.Api.Data;
using Firin.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Firin.Api.Endpoints;

public static class UserAdminEndpoints
{
    public record UserDto(int Id, string Name, Role Role, bool IsActive);
    public record UserCreateInput(string Name, string Pin, Role? Role);
    public record UserUpdateInput(string Name, Role? Role, bool? IsActive, string? Pin);

    private static readonly SemaphoreSlim OwnerGate = new(1, 1);

    private static Role RequireRole(Role? role) =>
        role is { } r && Enum.IsDefined(r) ? r : throw new DomainException("rol_gecersiz", "Rol geçersiz.");

    private static UserDto ToDto(User u) => new(u.Id, u.Name, u.Role, u.IsActive);

    public static void MapUserAdminEndpoints(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/admin/users").RequireAuthorization(AuthSetup.OwnerPolicy);

        g.MapGet("/", async (AppDb db) =>
            (await db.Users.AsNoTracking().OrderBy(u => u.Name).ToListAsync()).Select(ToDto).ToList());

        g.MapPost("/", async (UserCreateInput input, AppDb db, PinHasher hasher) =>
        {
            var name = await UniqueNameAsync(db, input.Name, null);
            if (!PinHasher.IsValidFormat(input.Pin)) throw AuthEndpoints.PinFormatError();
            var role = RequireRole(input.Role);
            var u = new User { Name = name, PinHash = hasher.Hash(input.Pin), Role = role };
            db.Users.Add(u);
            await db.SaveChangesAsync();
            return ToDto(u);
        });

        g.MapPut("/{id:int}", async (int id, UserUpdateInput input, AppDb db, PinHasher hasher, PinLockout lockout) =>
        {
            var role = RequireRole(input.Role);
            var isActive = input.IsActive ?? throw new DomainException("aktiflik_gecersiz", "Aktiflik durumu belirtilmeli.");

            await OwnerGate.WaitAsync();
            try
            {
                var u = await db.Users.FindAsync(id) ?? throw Validate.NotFound();

                var losesOwner = u is { Role: Role.Owner, IsActive: true } && (role != Role.Owner || !isActive);
                if (losesOwner && !await db.Users.AnyAsync(x => x.Id != id && x.Role == Role.Owner && x.IsActive))
                    throw new DomainException("son_sahip", "En az bir aktif sahip hesabı kalmalı.", StatusCodes.Status409Conflict);

                u.Name = await UniqueNameAsync(db, input.Name, id);
                u.Role = role;
                u.IsActive = isActive;
                var pinChanged = !string.IsNullOrEmpty(input.Pin);
                if (pinChanged)
                {
                    if (!PinHasher.IsValidFormat(input.Pin)) throw AuthEndpoints.PinFormatError();
                    u.PinHash = hasher.Hash(input.Pin!);
                }
                await db.SaveChangesAsync();
                if (pinChanged)
                {
                    using var _ = await lockout.AcquireAsync(id);
                    lockout.Success(id); // yeni PIN verilince kilit kalkar
                }
                return ToDto(u);
            }
            finally { OwnerGate.Release(); }
        });
    }

    private static async Task<string> UniqueNameAsync(AppDb db, string? raw, int? selfId)
    {
        var name = Validate.Name(raw, 30);
        if (await db.Users.AnyAsync(u => u.Name == name && u.Id != selfId))
            throw new DomainException("kullanici_adi_var", "Bu adda bir kullanıcı zaten var.", StatusCodes.Status409Conflict);
        return name;
    }
}
