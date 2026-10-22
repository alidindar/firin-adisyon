using System.Text.Json.Serialization;
using Firin.Api;
using Firin.Api.Auth;
using Firin.Api.Data;
using Firin.Api.Endpoints;
using Firin.Api.Orders;
using Firin.Api.Realtime;
using Firin.Api.Reports;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService(o => o.ServiceName = "FirinAdisyon");

builder.Services.AddSingleton<FirinPaths>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddDbContext<AppDb>((sp, o) =>
    o.UseSqlite(sp.GetRequiredService<FirinPaths>().ConnectionString));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<DomainExceptionHandler>();
builder.Services.AddFirinAuth();
builder.Services.AddSignalR();
builder.Services.AddSingleton<IRealtime, SignalRRealtime>();
builder.Services.AddSingleton<OrderLock>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<PaymentService>();
// Saat dilimi açılışta bir kez çözülür; yanlış kimlik her istekte 500 yerine açılışta net bir hatayla durdurur.
builder.Services.AddSingleton(sp => ResolveTimeZone(sp.GetRequiredService<IConfiguration>()["Firin:TimeZone"]));
builder.Services.AddScoped<ReportService>();
// Production'da da bozuk istek gövdesi DomainExceptionHandler'a ulaşsın (varsayılan yalnızca Development).
builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();
var paths = app.Services.GetRequiredService<FirinPaths>(); // klasörleri hemen oluştur
app.Services.GetRequiredService<TimeZoneInfo>(); // yanlış Firin:TimeZone burada patlasın
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    db.Database.Migrate();
    // WAL: yedek ve raporlar yazanları bloklamaz. Kalıcı ayardır (dosyada durur). synchronous=FULL kalır: para satırları elektrik kesilince de durmalı.
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");
}

// index.html asla önbelleğe alınmasın; yayından sonra eski sayfa silinmiş hash'li dosyalara bakmasın.
var staticFiles = new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        if (ctx.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase))
            ctx.Context.Response.Headers.CacheControl = "no-cache";
    },
};
app.UseDefaultFiles();
app.UseStaticFiles(staticFiles);
// Dosya adı her yüklemede değiştiği için kalıcı önbellek güvenli.
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(paths.PhotosDir),
    RequestPath = "/photos",
    OnPrepareResponse = c => c.Context.Response.Headers.CacheControl = "public,max-age=31536000,immutable",
});
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

var api = app.MapGroup("/api");
api.MapGet("/health", () => Results.Ok(new { status = "ok" }));
api.MapAuthEndpoints();
api.MapCatalogEndpoints();
api.MapPhotoEndpoints();
api.MapTableAdminEndpoints();
api.MapUserAdminEndpoints();
api.MapOrderEndpoints();
api.MapReportEndpoints();
api.MapFallback(() => Results.NotFound());
app.MapHub<AdisyonHub>("/hubs/adisyon");
app.MapFallbackToFile("index.html", staticFiles);

app.Run();

public partial class Program
{
    internal static TimeZoneInfo ResolveTimeZone(string? configured)
    {
        var id = string.IsNullOrWhiteSpace(configured) ? "Europe/Istanbul" : configured;
        try { return TimeZoneInfo.FindSystemTimeZoneById(id); }
        catch (TimeZoneNotFoundException) when (id == "Europe/Istanbul")
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Turkey Standard Time");
        }
        catch (TimeZoneNotFoundException ex)
        {
            throw new InvalidOperationException($"Firin:TimeZone geçersiz: '{id}'. Örnek: Europe/Istanbul", ex);
        }
    }
}
