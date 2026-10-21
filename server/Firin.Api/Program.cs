using System.Text.Json.Serialization;
using Firin.Api;
using Firin.Api.Auth;
using Firin.Api.Data;
using Firin.Api.Endpoints;
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
builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var app = builder.Build();
var paths = app.Services.GetRequiredService<FirinPaths>(); // klasörleri hemen oluştur
using (var scope = app.Services.CreateScope())
    scope.ServiceProvider.GetRequiredService<AppDb>().Database.Migrate();

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
api.MapFallback(() => Results.NotFound());
app.MapFallbackToFile("index.html", staticFiles);

app.Run();

public partial class Program;
