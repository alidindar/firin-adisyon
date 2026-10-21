using System.Security.Claims;
using Firin.Api.Data;
using Firin.Api.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

namespace Firin.Api.Auth;

public static class AuthSetup
{
    public const string OwnerPolicy = "Owner";
    private const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;

    public static IServiceCollection AddFirinAuth(this IServiceCollection services)
    {
        services.AddSingleton<PinHasher>();
        services.AddSingleton<PinLockout>();
        services.AddAuthentication(Scheme).AddCookie(o =>
        {
            o.Cookie.Name = "firin_oturum";
            o.Cookie.HttpOnly = true;
            o.Cookie.SameSite = SameSiteMode.Strict;
            o.ExpireTimeSpan = TimeSpan.FromDays(30);
            o.SlidingExpiration = true;
            // API: login sayfasına yönlendirme yerine durum kodu
            o.Events.OnRedirectToLogin = c => { c.Response.StatusCode = StatusCodes.Status401Unauthorized; return Task.CompletedTask; };
            o.Events.OnRedirectToAccessDenied = c => { c.Response.StatusCode = StatusCodes.Status403Forbidden; return Task.CompletedTask; };
            // Pasife alınan ya da rolü değişen kullanıcının oturumu hemen düşer.
            o.Events.OnValidatePrincipal = async c =>
            {
                var id = c.Principal?.UserIdOrNull();
                var db = c.HttpContext.RequestServices.GetRequiredService<AppDb>();
                var user = id is null ? null : await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id);
                if (user is null || !user.IsActive || user.Role.ToString() != c.Principal!.FindFirstValue(ClaimTypes.Role))
                {
                    c.RejectPrincipal();
                    await c.HttpContext.SignOutAsync(Scheme);
                }
            };
        });
        services.AddAuthorizationBuilder()
            .AddPolicy(OwnerPolicy, p => p.RequireRole(nameof(Role.Owner)));
        return services;
    }

    public static Task SignInAsync(HttpContext ctx, User user)
    {
        Claim[] claims =
        [
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.Name),
            new(ClaimTypes.Role, user.Role.ToString()),
        ];
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme));
        return ctx.SignInAsync(Scheme, principal, new AuthenticationProperties { IsPersistent = true });
    }

    public static int? UserIdOrNull(this ClaimsPrincipal p) =>
        int.TryParse(p.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public static int UserId(this ClaimsPrincipal p) =>
        p.UserIdOrNull() ?? throw new InvalidOperationException("Oturum yok.");
}
