using System.Security.Claims;
using Firin.Api.Auth;
using Firin.Api.Data;
using Firin.Api.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Firin.Api.Endpoints;

public static class AuthEndpoints
{
    public record UserOptionDto(int Id, string Name);
    public record AuthStatusDto(bool NeedsSetup, List<UserOptionDto> Users);
    public record SetupRequest(string Name, string Pin);
    public record LoginRequest(int UserId, string Pin);
    public record MeDto(int Id, string Name, Role Role);

    public static DomainException PinFormatError() => new("pin_gecersiz", "PIN 4 haneli bir sayı olmalı.");

    public static void MapAuthEndpoints(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/auth");

        g.MapGet("/status", async (AppDb db) =>
        {
            var users = await db.Users.Where(u => u.IsActive).OrderBy(u => u.Name)
                .Select(u => new UserOptionDto(u.Id, u.Name)).ToListAsync();
            return new AuthStatusDto(!await db.Users.AnyAsync(), users);
        });

        g.MapPost("/setup", async (SetupRequest req, AppDb db, PinHasher hasher, HttpContext ctx) =>
        {
            if (await db.Users.AnyAsync())
                throw new DomainException("kurulum_yapilmis", "Kurulum zaten yapılmış.", StatusCodes.Status409Conflict);
            var name = req.Name?.Trim();
            if (string.IsNullOrEmpty(name)) throw new DomainException("ad_gerekli", "Ad boş olamaz.");
            if (!PinHasher.IsValidFormat(req.Pin)) throw PinFormatError();

            var user = new User { Name = name, PinHash = hasher.Hash(req.Pin), Role = Role.Owner };
            db.Users.Add(user);
            await db.SaveChangesAsync();
            await AuthSetup.SignInAsync(ctx, user);
            return new MeDto(user.Id, user.Name, user.Role);
        });

        g.MapPost("/login", async (LoginRequest req, AppDb db, PinHasher hasher, PinLockout lockout, HttpContext ctx) =>
        {
            var user = await db.Users.FirstOrDefaultAsync(u => u.Id == req.UserId && u.IsActive)
                ?? throw new DomainException("kullanici_yok", "Kullanıcı bulunamadı.", StatusCodes.Status404NotFound);
            if (lockout.RemainingLock(user.Id) is { } left)
                throw new DomainException("kilitli",
                    $"Çok fazla yanlış deneme. {Math.Ceiling(left.TotalSeconds)} saniye sonra tekrar deneyin.",
                    StatusCodes.Status423Locked);
            if (!PinHasher.IsValidFormat(req.Pin) || !hasher.Verify(user.PinHash, req.Pin))
            {
                lockout.Fail(user.Id);
                throw new DomainException("pin_hatali", "PIN hatalı.", StatusCodes.Status401Unauthorized);
            }
            lockout.Success(user.Id);
            await AuthSetup.SignInAsync(ctx, user);
            return new MeDto(user.Id, user.Name, user.Role);
        });

        g.MapGet("/me", async (ClaimsPrincipal p, AppDb db) =>
            await db.Users.FindAsync(p.UserId()) is { } u
                ? Results.Ok(new MeDto(u.Id, u.Name, u.Role))
                : Results.Unauthorized())
            .RequireAuthorization();

        g.MapPost("/logout", async (HttpContext ctx) =>
        {
            await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        });
    }
}
