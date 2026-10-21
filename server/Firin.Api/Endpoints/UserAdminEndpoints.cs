using Firin.Api.Auth;
using Firin.Api.Data;
using Firin.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Firin.Api.Endpoints;

public static class UserAdminEndpoints
{
    public record UserDto(int Id, string Name, Role Role, bool IsActive);
    public record UserCreateInput(string Name, string Pin, Role Role);
    public record UserUpdateInput(string Name, Role Role, bool IsActive, string? Pin);

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
            if (!Enum.IsDefined(input.Role)) throw new DomainException("rol_gecersiz", "Rol geçersiz.");
            var u = new User { Name = name, PinHash = hasher.Hash(input.Pin), Role = input.Role };
            db.Users.Add(u);
            await db.SaveChangesAsync();
            return ToDto(u);
        });

        g.MapPut("/{id:int}", async (int id, UserUpdateInput input, AppDb db, PinHasher hasher, PinLockout lockout) =>
        {
            var u = await db.Users.FindAsync(id) ?? throw Validate.NotFound();
            if (!Enum.IsDefined(input.Role)) throw new DomainException("rol_gecersiz", "Rol geçersiz.");

            var losesOwner = u is { Role: Role.Owner, IsActive: true } && (input.Role != Role.Owner || !input.IsActive);
            if (losesOwner && !await db.Users.AnyAsync(x => x.Id != id && x.Role == Role.Owner && x.IsActive))
                throw new DomainException("son_sahip", "En az bir aktif sahip hesabı kalmalı.", StatusCodes.Status409Conflict);

            u.Name = await UniqueNameAsync(db, input.Name, id);
            u.Role = input.Role;
            u.IsActive = input.IsActive;
            if (!string.IsNullOrEmpty(input.Pin))
            {
                if (!PinHasher.IsValidFormat(input.Pin)) throw AuthEndpoints.PinFormatError();
                u.PinHash = hasher.Hash(input.Pin);
                lockout.Success(id); // yeni PIN verilince kilit kalkar
            }
            await db.SaveChangesAsync();
            return ToDto(u);
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
